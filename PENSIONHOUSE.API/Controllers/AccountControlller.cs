using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

public record UpdateProfileRequest(string FullName, string? ContactNo);
public record ChangePasswordRequest(string Otp, string NewPassword);

public class MeResponse
{
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string? ContactNo { get; set; }
}

public class PwAccountRow
{
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
}

public class PwOtpRow
{
    public int Id { get; set; }
    public byte[] CodeHash { get; set; } = Array.Empty<byte>();
    public int Attempts { get; set; }
}

public class PwOtpStats
{
    public int SecondsSinceLast { get; set; }
    public int SentLastHour { get; set; }
}

[ApiController]
[Authorize(Roles = "staff,admin")]
[Route("api/account")]
public class AccountController : ControllerBase
{
    private const int OtpMinutes = 10;        // how long a code stays valid
    private const int ResendSeconds = 60;     // minimum gap between two codes
    private const int MaxSendsPerHour = 5;    // codes per account per hour
    private const int MaxAttempts = 5;        // wrong guesses before the code is locked

    private readonly string _connStr;
    private readonly byte[] _otpKey;
    private readonly IEmailSender _email;
    private readonly IAuditRepository _audit;
    private readonly ILogger<AccountController> _log;

    public AccountController(IConfiguration config, IEmailSender email, IAuditRepository audit, ILogger<AccountController> log)
    {
        _connStr = config.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' not found.");
        _otpKey = Encoding.UTF8.GetBytes(config["Jwt:Key"]
            ?? throw new InvalidOperationException("Jwt:Key not found."));
        _email = email;
        _audit = audit;
        _log = log;
    }

    private SqlConnection Conn() => new SqlConnection(_connStr);

    // Who is calling: taken from the JWT, never from the request body
    private int UserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private bool IsAdmin => string.Equals(User.FindFirstValue(ClaimTypes.Role), "admin", StringComparison.OrdinalIgnoreCase);

    // Fixed strings only (never user input), so they are safe to put into SQL text
    private string AccountRoleKey => IsAdmin ? "admin" : "staff";
    private string AccountTable => IsAdmin ? "Admins" : "Staff";

    // The code is stored as a keyed hash tied to this exact account, so a copied row is useless elsewhere
    private byte[] HashCode(string code)
    {
        using var h = new HMACSHA256(_otpKey);
        return h.ComputeHash(Encoding.UTF8.GetBytes($"{AccountRoleKey}:{UserId}:{code}"));
    }

    private static string MaskEmail(string email)
    {
        var at = email.IndexOf('@');
        if (at <= 1) return email;
        var local = email[..at];
        var keep = Math.Min(2, local.Length);
        return local[..keep] + new string('•', Math.Max(local.Length - keep, 3)) + email[at..];
    }

    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        using var conn = Conn();
        var sql = IsAdmin
            ? "SELECT FullName, Email, CAST(NULL AS nvarchar(20)) AS ContactNo FROM Admins WHERE Id = @Id"
            : "SELECT FullName, Email, ContactNo FROM Staff WHERE Id = @Id";

        var me = await conn.QuerySingleOrDefaultAsync<MeResponse>(sql, new { Id = UserId });
        return me is null ? NotFound(new { message = "Account not found." }) : Ok(me);
    }

    [HttpPut("me")]
    public async Task<IActionResult> UpdateMe(UpdateProfileRequest req)
    {
        var name = req.FullName?.Trim() ?? "";
        if (name.Length == 0 || name.Length > 100)
            return BadRequest(new { message = "Enter your full name (up to 100 characters)." });

        var contact = string.IsNullOrWhiteSpace(req.ContactNo) ? null : req.ContactNo.Trim();
        if (contact is { Length: > 20 })
            return BadRequest(new { message = "The mobile number is too long." });

        using var conn = Conn();
        var rows = IsAdmin
            ? await conn.ExecuteAsync("UPDATE Admins SET FullName = @Name WHERE Id = @Id",
                  new { Name = name, Id = UserId })
            : await conn.ExecuteAsync("UPDATE Staff SET FullName = @Name, ContactNo = @Contact WHERE Id = @Id",
                  new { Name = name, Contact = contact, Id = UserId });

        return rows == 0 ? NotFound(new { message = "Account not found." }) : Ok(new { message = "Profile updated." });
    }

    // ---------- Step 1: email a 6-digit code (admin and staff) ----------
    [HttpPost("password/otp")]
    public async Task<IActionResult> SendPasswordOtp()
    {
        using var conn = Conn();

        var account = await conn.QuerySingleOrDefaultAsync<PwAccountRow>(
            $"SELECT FullName, Email, CAST('' AS nvarchar(10)) AS PasswordHash FROM {AccountTable} WHERE Id = @Id",
            new { Id = UserId });
        if (account is null || string.IsNullOrWhiteSpace(account.Email))
            return NotFound(new { message = "Account not found." });

        var stats = await conn.QuerySingleAsync<PwOtpStats>(@"
            SELECT ISNULL(DATEDIFF(SECOND, MAX(CreatedAt), SYSDATETIME()), 9999) AS SecondsSinceLast,
                   COUNT(*) AS SentLastHour
            FROM PasswordOtps
            WHERE AccountRole = @Role AND AccountId = @Id
              AND CreatedAt >= DATEADD(HOUR, -1, SYSDATETIME())",
            new { Role = AccountRoleKey, Id = UserId });

        if (stats.SecondsSinceLast < ResendSeconds)
        {
            var wait = ResendSeconds - stats.SecondsSinceLast;
            return StatusCode(429, new { message = $"Please wait {wait} seconds before asking for another code.", retryAfterSeconds = wait });
        }
        if (stats.SentLastHour >= MaxSendsPerHour)
            return StatusCode(429, new { message = "Too many codes requested. Please try again in an hour.", retryAfterSeconds = 3600 });

        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

        await conn.OpenAsync();
        int otpId;
        using (var tx = conn.BeginTransaction())
        {
            // Only the newest code can ever work
            await conn.ExecuteAsync(
                "UPDATE PasswordOtps SET UsedAt = SYSDATETIME() WHERE AccountRole = @Role AND AccountId = @Id AND UsedAt IS NULL",
                new { Role = AccountRoleKey, Id = UserId }, tx);

            otpId = await conn.ExecuteScalarAsync<int>(@"
                INSERT INTO PasswordOtps (AccountRole, AccountId, CodeHash, ExpiresAt, IpAddress)
                OUTPUT INSERTED.Id
                VALUES (@Role, @Id, @Hash, DATEADD(MINUTE, @Min, SYSDATETIME()), @Ip)",
                new
                {
                    Role = AccountRoleKey,
                    Id = UserId,
                    Hash = HashCode(code),
                    Min = OtpMinutes,
                    Ip = HttpContext.Connection.RemoteIpAddress?.ToString()
                }, tx);

            tx.Commit();
        }

        try
        {
            var (html, text) = EmailTemplates.PasswordCode(account.FullName, code, OtpMinutes);
            await _email.SendAsync(account.Email, account.FullName,
                "Your Pension House verification code", html, text, HttpContext.RequestAborted);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Could not send the password code to account {Role}/{Id}", AccountRoleKey, UserId);
            // The code was never delivered, so remove it (and do not make the user wait 60 seconds)
            await conn.ExecuteAsync("DELETE FROM PasswordOtps WHERE Id = @Id", new { Id = otpId });
            return StatusCode(503, new { message = "We couldn’t send the email right now. Please try again in a moment." });
        }

        await _audit.LogAsync(this, "Settings", "Requested a password change code");

        var masked = MaskEmail(account.Email);
        return Ok(new
        {
            message = $"We sent a 6-digit code to {masked}.",
            maskedEmail = masked,
            expiresInSeconds = OtpMinutes * 60,
            resendInSeconds = ResendSeconds
        });
    }

    // ---------- Step 2: check the code and set the new password (admin and staff) ----------
    [HttpPatch("password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest req)
    {
        var otp = (req.Otp ?? "").Trim();
        var neu = req.NewPassword ?? "";

        if (!Regex.IsMatch(otp, "^[0-9]{6}$"))
            return BadRequest(new { message = "Enter the 6-digit code we emailed you." });

        // 8 to 32 characters, the same rule everywhere (see PasswordRules.cs)
        var pwError = PasswordRules.Check(neu);
        if (pwError != null)
            return BadRequest(new { message = pwError });

        using var conn = Conn();
        await conn.OpenAsync();

        var account = await conn.QuerySingleOrDefaultAsync<PwAccountRow>(
            $"SELECT FullName, Email, PasswordHash FROM {AccountTable} WHERE Id = @Id", new { Id = UserId });
        if (account is null)
            return NotFound(new { message = "Account not found." });

        // Checked before the code, so this mistake never costs an attempt
        if (BCrypt.Net.BCrypt.Verify(neu, account.PasswordHash))
            return BadRequest(new { message = "Choose a password that is different from your current one." });

        var newHash = BCrypt.Net.BCrypt.HashPassword(neu);   // slow on purpose, so do it before locking rows

        using var tx = conn.BeginTransaction();

        var row = await conn.QuerySingleOrDefaultAsync<PwOtpRow>(@"
            SELECT TOP 1 Id, CodeHash, Attempts
            FROM PasswordOtps WITH (UPDLOCK, ROWLOCK)
            WHERE AccountRole = @Role AND AccountId = @Id
              AND UsedAt IS NULL AND ExpiresAt > SYSDATETIME()
            ORDER BY CreatedAt DESC, Id DESC",
            new { Role = AccountRoleKey, Id = UserId }, tx);

        if (row is null)
            return BadRequest(new
            {
                message = "That code has expired or was already used. Request a new one.",
                codeInvalidated = true
            });

        if (!CryptographicOperations.FixedTimeEquals(row.CodeHash, HashCode(otp)))
        {
            var attempts = row.Attempts + 1;
            var locked = attempts >= MaxAttempts;

            await conn.ExecuteAsync(
                "UPDATE PasswordOtps SET Attempts = @A, UsedAt = CASE WHEN @Locked = 1 THEN SYSDATETIME() ELSE UsedAt END WHERE Id = @OtpId",
                new { A = attempts, Locked = locked ? 1 : 0, OtpId = row.Id }, tx);
            tx.Commit();

            await _audit.LogAsync(this, "Settings", "Failed password change (wrong code)", true);

            return BadRequest(new
            {
                message = locked
                    ? "Too many wrong codes. Request a new code."
                    : $"That code isn’t correct. {MaxAttempts - attempts} attempt(s) left.",
                codeInvalidated = locked
            });
        }

        await conn.ExecuteAsync($"UPDATE {AccountTable} SET PasswordHash = @Hash WHERE Id = @Id",
            new { Hash = newHash, Id = UserId }, tx);
        await conn.ExecuteAsync(
            "UPDATE PasswordOtps SET UsedAt = SYSDATETIME() WHERE AccountRole = @Role AND AccountId = @Id AND UsedAt IS NULL",
            new { Role = AccountRoleKey, Id = UserId }, tx);
        tx.Commit();

        await _audit.LogAsync(this, "Settings", "Changed their password");

        // Best effort: the password is already changed, so a mail problem must not turn this into an error
        try
        {
            var (html, text) = EmailTemplates.PasswordChanged(account.FullName, DateTime.Now);
            await _email.SendAsync(account.Email, account.FullName,
                "Your Pension House password was changed", html, text, HttpContext.RequestAborted);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Password changed but the notice email failed for {Role}/{Id}", AccountRoleKey, UserId);
        }

        return Ok(new { message = "Password changed." });
    }
}