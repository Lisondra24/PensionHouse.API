using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

public record GuestForgotRequest(string Email);
public record GuestResetRequest(string Email, string Otp, string NewPassword);

/// Forgot password for GUESTS (the mobile app). Staff and admin already have theirs in AuthController.
[ApiController]
[AllowAnonymous]
[Route("api/guest-auth")]
public class GuestPasswordController : ControllerBase
{
    // The table that holds the guest accounts. Change this if yours has another name.
    private const string GuestTable = "Users";

    private const int OtpMinutes = 10;        // how long a code stays valid
    private const int ResendSeconds = 60;     // minimum gap between two codes
    private const int MaxSendsPerHour = 5;    // codes per guest per hour
    private const int MaxAttempts = 5;        // wrong guesses before the code is locked

    private static readonly Regex EmailRx = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);

    private readonly IAuthRepository _auth;
    private readonly IEmailSender _email;
    private readonly ILogger<GuestPasswordController> _log;
    private readonly string _connStr;
    private readonly byte[] _otpKey;

    public GuestPasswordController(IAuthRepository auth, IEmailSender email, IConfiguration config,
                                   ILogger<GuestPasswordController> log)
    {
        _auth = auth;
        _email = email;
        _log = log;
        _connStr = config.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' not found.");
        _otpKey = Encoding.UTF8.GetBytes(config["Jwt:Key"]
            ?? throw new InvalidOperationException("Jwt:Key not found."));
    }

    private static string Normalize(string email) => email.Trim().ToLowerInvariant();
    private string? ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString();

    // The code is tied to this exact guest, so a copied row is useless for anyone else
    private byte[] HashCode(int userId, string code)
    {
        using var h = new HMACSHA256(_otpKey);
        return h.ComputeHash(Encoding.UTF8.GetBytes($"guest:{userId}:{code}"));
    }

    // Finds an ENABLED guest by email (uses the same stored procedure as the app's login)
    private async Task<Account?> FindGuestAsync(string email)
    {
        var accounts = await _auth.GetLoginAccountsAsync(email, LoginType.Mobile);
        return accounts.FirstOrDefault(a =>
            string.Equals(a.Role, "user", StringComparison.OrdinalIgnoreCase) && a.IsActive);
    }

    // Same answer whether or not the email has an account, so nobody can use this to find valid emails
    private IActionResult ForgotReply() => Ok(new
    {
        message = "If that email has an account, a 6-digit code is on its way.",
        expiresInSeconds = OtpMinutes * 60,
        resendInSeconds = ResendSeconds
    });

    // Step 1: email a 6-digit code
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(GuestForgotRequest req)
    {
        var email = Normalize(req.Email ?? "");
        if (email.Length == 0 || email.Length > 150 || !EmailRx.IsMatch(email))
            return BadRequest(new { message = "Enter a valid email address." });

        var guest = await FindGuestAsync(email);
        if (guest is null) return ForgotReply();

        using var conn = new SqlConnection(_connStr);
        await conn.OpenAsync();

        // Too soon or too many: quietly do nothing (the reply stays the same)
        var stats = await conn.QuerySingleAsync<PwOtpStats>(@"
            SELECT ISNULL(DATEDIFF(SECOND, MAX(CreatedAt), SYSDATETIME()), 9999) AS SecondsSinceLast,
                   COUNT(*) AS SentLastHour
            FROM GuestPasswordOtps
            WHERE UserId = @Id AND CreatedAt >= DATEADD(HOUR, -1, SYSDATETIME())",
            new { Id = guest.Id });

        if (stats.SecondsSinceLast < ResendSeconds || stats.SentLastHour >= MaxSendsPerHour)
            return ForgotReply();

        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

        int otpId;
        using (var tx = conn.BeginTransaction())
        {
            // Only the newest code can ever work
            await conn.ExecuteAsync(
                "UPDATE GuestPasswordOtps SET UsedAt = SYSDATETIME() WHERE UserId = @Id AND UsedAt IS NULL",
                new { Id = guest.Id }, tx);

            otpId = await conn.ExecuteScalarAsync<int>(@"
                INSERT INTO GuestPasswordOtps (UserId, CodeHash, ExpiresAt, IpAddress)
                OUTPUT INSERTED.Id
                VALUES (@Id, @Hash, DATEADD(MINUTE, @Min, SYSDATETIME()), @Ip)",
                new { Id = guest.Id, Hash = HashCode(guest.Id, code), Min = OtpMinutes, Ip = ClientIp }, tx);

            tx.Commit();
        }

        // Sent in the background so the reply takes the same time for every email
        _ = SendCodeAsync(guest, code, otpId);

        return ForgotReply();
    }

    private async Task SendCodeAsync(Account guest, string code, int otpId)
    {
        try
        {
            var (html, text) = EmailTemplates.PasswordCode(guest.FullName, code, OtpMinutes);
            await _email.SendAsync(guest.Email, guest.FullName,
                "Your Pension House verification code", html, text);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Could not send the password reset code to guest {Id}", guest.Id);
            try
            {
                // The code never arrived, so remove it (the guest can ask again straight away)
                using var conn = new SqlConnection(_connStr);
                await conn.ExecuteAsync("DELETE FROM GuestPasswordOtps WHERE Id = @Id", new { Id = otpId });
            }
            catch
            {
            }
        }
    }

    // Step 2: check the code and set the new password
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(GuestResetRequest req)
    {
        var email = Normalize(req.Email ?? "");
        var otp = (req.Otp ?? "").Trim();
        var neu = req.NewPassword ?? "";

        if (!Regex.IsMatch(otp, "^[0-9]{6}$"))
            return BadRequest(new { message = "Enter the 6-digit code we emailed you." });

        var pwError = PasswordRules.Check(neu);
        if (pwError != null)
            return BadRequest(new { message = pwError });

        // Every failure says the same thing, so this cannot be used to find valid emails or count attempts
        var wrongCode = BadRequest(new { message = "That code isn’t correct or has expired. Check it, or request a new one." });

        // Slow on purpose, and done for every request so the timing does not reveal whether the email exists
        var newHash = BCrypt.Net.BCrypt.HashPassword(neu);

        var guest = await FindGuestAsync(email);
        if (guest is null) return wrongCode;

        using var conn = new SqlConnection(_connStr);
        await conn.OpenAsync();
        using var tx = conn.BeginTransaction();

        var row = await conn.QuerySingleOrDefaultAsync<PwOtpRow>(@"
            SELECT TOP 1 Id, CodeHash, Attempts
            FROM GuestPasswordOtps WITH (UPDLOCK, ROWLOCK)
            WHERE UserId = @Id AND UsedAt IS NULL AND ExpiresAt > SYSDATETIME()
            ORDER BY CreatedAt DESC, Id DESC",
            new { Id = guest.Id }, tx);

        if (row is null) return wrongCode;

        if (!CryptographicOperations.FixedTimeEquals(row.CodeHash, HashCode(guest.Id, otp)))
        {
            var attempts = row.Attempts + 1;
            var locked = attempts >= MaxAttempts;

            await conn.ExecuteAsync(
                "UPDATE GuestPasswordOtps SET Attempts = @A, UsedAt = CASE WHEN @Locked = 1 THEN SYSDATETIME() ELSE UsedAt END WHERE Id = @OtpId",
                new { A = attempts, Locked = locked ? 1 : 0, OtpId = row.Id }, tx);
            tx.Commit();

            return wrongCode;
        }

        // Checked only after the code is correct, so it can never be used to guess someone's current password
        if (BCrypt.Net.BCrypt.Verify(neu, guest.PasswordHash))
            return BadRequest(new { message = "Choose a password that is different from your current one." });

        await conn.ExecuteAsync($"UPDATE {GuestTable} SET PasswordHash = @Hash WHERE Id = @Id",
            new { Hash = newHash, Id = guest.Id }, tx);
        await conn.ExecuteAsync(
            "UPDATE GuestPasswordOtps SET UsedAt = SYSDATETIME() WHERE UserId = @Id AND UsedAt IS NULL",
            new { Id = guest.Id }, tx);
        tx.Commit();

        // Best effort: the password is already changed, so a mail problem must not turn this into an error
        try
        {
            var (html, text) = EmailTemplates.PasswordChanged(guest.FullName, DateTime.Now);
            await _email.SendAsync(guest.Email, guest.FullName,
                "Your Pension House password was changed", html, text);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Password reset but the notice email failed for guest {Id}", guest.Id);
        }

        return Ok(new { message = "Password changed." });
    }
}