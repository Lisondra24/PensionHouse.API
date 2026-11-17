using System.Security.Cryptography;
using System.Text;
using Dapper;
using Microsoft.Data.SqlClient;

/// Sends and checks the 6-digit code that confirms a new guest owns their email.
public class EmailVerificationService
{
    public const int OtpMinutes = 10;        // how long a code stays valid
    public const int ResendSeconds = 60;     // minimum gap between two codes
    public const int MaxSendsPerHour = 5;    // codes per guest per hour
    public const int MaxAttempts = 5;        // wrong guesses before the code is locked

    private const string GuestTable = "Users";

    private readonly string _connStr;
    private readonly byte[] _key;
    private readonly IEmailSender _email;
    private readonly ILogger<EmailVerificationService> _log;

    public EmailVerificationService(IConfiguration config, IEmailSender email,
                                    ILogger<EmailVerificationService> log)
    {
        _email = email;
        _log = log;
        _connStr = config.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' not found.");
        _key = Encoding.UTF8.GetBytes(config["Jwt:Key"]
            ?? throw new InvalidOperationException("Jwt:Key not found."));
    }

    // Tied to this exact guest, so a copied row is useless for anyone else
    private byte[] Hash(int userId, string code)
    {
        using var h = new HMACSHA256(_key);
        return h.ComputeHash(Encoding.UTF8.GetBytes($"verify:{userId}:{code}"));
    }

    public async Task<bool> IsVerifiedAsync(int userId)
    {
        using var conn = new SqlConnection(_connStr);
        return await conn.QuerySingleOrDefaultAsync<bool?>(
            $"SELECT EmailVerified FROM {GuestTable} WHERE Id = @Id", new { Id = userId }) ?? false;
    }

    /// Creates a code and emails it. Returns false when it is too soon or too many were sent.
    public async Task<bool> SendCodeAsync(int userId, string toEmail, string fullName, string? ip)
    {
        using var conn = new SqlConnection(_connStr);
        await conn.OpenAsync();

        var stats = await conn.QuerySingleAsync<PwOtpStats>(@"
            SELECT ISNULL(DATEDIFF(SECOND, MAX(CreatedAt), SYSDATETIME()), 9999) AS SecondsSinceLast,
                   COUNT(*) AS SentLastHour
            FROM GuestEmailOtps
            WHERE UserId = @Id AND CreatedAt >= DATEADD(HOUR, -1, SYSDATETIME())",
            new { Id = userId });

        if (stats.SecondsSinceLast < ResendSeconds || stats.SentLastHour >= MaxSendsPerHour)
            return false;

        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

        int otpId;
        using (var tx = conn.BeginTransaction())
        {
            // Only the newest code can ever work
            await conn.ExecuteAsync(
                "UPDATE GuestEmailOtps SET UsedAt = SYSDATETIME() WHERE UserId = @Id AND UsedAt IS NULL",
                new { Id = userId }, tx);

            otpId = await conn.ExecuteScalarAsync<int>(@"
                INSERT INTO GuestEmailOtps (UserId, CodeHash, ExpiresAt, IpAddress)
                OUTPUT INSERTED.Id
                VALUES (@Id, @Hash, DATEADD(MINUTE, @Min, SYSDATETIME()), @Ip)",
                new { Id = userId, Hash = Hash(userId, code), Min = OtpMinutes, Ip = ip }, tx);

            tx.Commit();
        }

        // Sent in the background so the reply is just as fast for every email
        _ = DeliverAsync(toEmail, fullName, code, otpId);
        return true;
    }

    private async Task DeliverAsync(string toEmail, string fullName, string code, int otpId)
    {
        try
        {
            var (html, text) = EmailTemplates.VerifyEmailCode(fullName, code, OtpMinutes);
            await _email.SendAsync(toEmail, fullName, "Verify your Pension House email", html, text);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Could not send the verification code (otp {Id})", otpId);
            try
            {
                // The code never arrived, so remove it (the guest can ask again straight away)
                using var conn = new SqlConnection(_connStr);
                await conn.ExecuteAsync("DELETE FROM GuestEmailOtps WHERE Id = @Id", new { Id = otpId });
            }
            catch { }
        }
    }

    /// True when the code is right. The account is then marked as verified.
    public async Task<bool> VerifyAsync(int userId, string code)
    {
        using var conn = new SqlConnection(_connStr);
        await conn.OpenAsync();
        using var tx = conn.BeginTransaction();

        var row = await conn.QuerySingleOrDefaultAsync<PwOtpRow>(@"
            SELECT TOP 1 Id, CodeHash, Attempts
            FROM GuestEmailOtps WITH (UPDLOCK, ROWLOCK)
            WHERE UserId = @Id AND UsedAt IS NULL AND ExpiresAt > SYSDATETIME()
            ORDER BY CreatedAt DESC, Id DESC",
            new { Id = userId }, tx);

        if (row is null) return false;

        if (!CryptographicOperations.FixedTimeEquals(row.CodeHash, Hash(userId, code)))
        {
            var attempts = row.Attempts + 1;
            var locked = attempts >= MaxAttempts;

            await conn.ExecuteAsync(
                "UPDATE GuestEmailOtps SET Attempts = @A, UsedAt = CASE WHEN @Locked = 1 THEN SYSDATETIME() ELSE UsedAt END WHERE Id = @OtpId",
                new { A = attempts, Locked = locked ? 1 : 0, OtpId = row.Id }, tx);
            tx.Commit();
            return false;
        }

        await conn.ExecuteAsync($"UPDATE {GuestTable} SET EmailVerified = 1 WHERE Id = @Id",
            new { Id = userId }, tx);
        await conn.ExecuteAsync(
            "UPDATE GuestEmailOtps SET UsedAt = SYSDATETIME() WHERE UserId = @Id AND UsedAt IS NULL",
            new { Id = userId }, tx);
        tx.Commit();
        return true;
    }
}