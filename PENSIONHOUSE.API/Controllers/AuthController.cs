using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

public record ForgotPasswordRequest(string Email);
public record ResetPasswordRequest(string Email, string Otp, string NewPassword);

public class ResetAccountRow
{
    public int Id { get; set; }
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Role { get; set; } = "";           // "staff" or "admin"
    public string PasswordHash { get; set; } = "";
}

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private const long MaxIdBytes = 5 * 1024 * 1024;   // 5 MB

    private const int OtpMinutes = 10;        // how long a code stays valid
    private const int ResendSeconds = 60;     // minimum gap between two codes
    private const int MaxSendsPerHour = 5;    // codes per account per hour
    private const int MaxAttempts = 5;        // wrong guesses before the code is locked

    private static readonly Regex EmailRx = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);

    private readonly IAuthRepository _repo;
    private readonly JwtService _jwt;
    private readonly IWebHostEnvironment _env;
    private readonly IEmailSender _email;
    private readonly IAuditRepository _audit;
    private readonly ILogger<AuthController> _log;
    private readonly string _connStr;
    private readonly byte[] _otpKey;
    private readonly LoginThrottle _throttle;

    public AuthController(IAuthRepository repo, JwtService jwt, IWebHostEnvironment env,
                          IConfiguration config, IEmailSender email, IAuditRepository audit,
                          ILogger<AuthController> log, LoginThrottle throttle)
    {
        _throttle = throttle;
        _repo = repo;
        _jwt = jwt;
        _env = env;
        _email = email;
        _audit = audit;
        _log = log;
        _connStr = config.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' not found.");
        _otpKey = Encoding.UTF8.GetBytes(config["Jwt:Key"]
            ?? throw new InvalidOperationException("Jwt:Key not found."));
    }

    private static string Normalize(string email) => email.Trim().ToLowerInvariant();

    private string? ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString();

    // Flutter calls this to fill the dropdown
    [AllowAnonymous]
    [HttpGet("id-types")]
    public IActionResult IdTypes() => Ok(PhilippineIds.Types);

    // MOBILE ONLY: register a guest (role is always USER, never taken from the request)
    [HttpPost("register")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(8_000_000)]
    public async Task<IActionResult> Register([FromForm] RegisterRequest req)
    {
        var idType = req.IdType.Trim();
        if (!PhilippineIds.IsValid(idType))
            return BadRequest(new { message = "Choose a valid ID type from the list." });

        var file = req.IdImage;
        if (file.Length == 0 || file.Length > MaxIdBytes)
            return BadRequest(new { message = "The ID photo must be between 1 byte and 5 MB." });

        var ext = await DetectImageExtensionAsync(file);
        if (ext is null)
            return BadRequest(new { message = "The ID photo must be a JPG or PNG image." });

        // Private folder, outside any public path. Random name, so nobody can guess it.
        var dir = Path.Combine(_env.ContentRootPath, "PrivateUploads", "ids");
        Directory.CreateDirectory(dir);
        var fileName = $"{Guid.NewGuid():N}{ext}";
        var fullPath = Path.Combine(dir, fileName);

        await using (var fs = System.IO.File.Create(fullPath))
            await file.CopyToAsync(fs);

        int id;
        try
        {
            var hash = BCrypt.Net.BCrypt.HashPassword(req.Password);
            id = await _repo.CreateAccountAsync(
                AccountRole.User,
                req.FullName.Trim(),
                Normalize(req.Email),
                hash,
                req.ContactNo?.Trim(),
                idType,
                fileName);
        }
        catch
        {
            System.IO.File.Delete(fullPath);
            throw;
        }

        if (id <= 0)
        {
            System.IO.File.Delete(fullPath);   // do not keep the photo of a failed registration
            return id == -1
                ? Conflict(new { message = "Email is already registered." })
                : BadRequest(new { message = "Could not create the account." });
        }

        return Ok(new { message = "Registration successful.", id });
    }

    // Checks the real file header, not just the file name
    private static async Task<string?> DetectImageExtensionAsync(IFormFile file)
    {
        var head = new byte[8];
        await using var s = file.OpenReadStream();
        var read = await s.ReadAsync(head.AsMemory(0, 8));
        if (read < 4) return null;

        if (head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF) return ".jpg";
        if (head[0] == 0x89 && head[1] == 0x50 && head[2] == 0x4E && head[3] == 0x47) return ".png";
        return null;
    }

    // MOBILE ONLY: guest login
    [HttpPost("login")]
    public Task<IActionResult> Login(LoginRequest req)
        => AuthenticateAsync(req, LoginType.Mobile);

    // WEB UI ONLY: staff first, then admin
    [HttpPost("web-login")]
    public Task<IActionResult> WebLogin(LoginRequest req)
        => AuthenticateAsync(req, LoginType.Web);

    private async Task<IActionResult> AuthenticateAsync(LoginRequest req, string loginType)
    {
        var email = Normalize(req.Email);
        var isWeb = loginType == LoginType.Web;   // only staff/admin sign-ins go into the audit log

        // Too many wrong passwords from this email + device: make them wait
        var wait = _throttle.SecondsLocked(email, ClientIp);
        if (wait > 0)
        {
            Response.Headers["Retry-After"] = wait.ToString();
            return StatusCode(429, new
            {
                message = $"Too many failed sign-ins. Please try again in {LoginThrottle.Describe(wait)}.",
                retryAfterSeconds = wait
            });
        }

        var accounts = (await _repo.GetLoginAccountsAsync(email, loginType)).ToList();

        // The SP returns staff before admin, so this keeps that priority
        var account = accounts.FirstOrDefault(a =>
            BCrypt.Net.BCrypt.Verify(req.Password, a.PasswordHash));

        if (account == null)
        {
            _throttle.Fail(email, ClientIp);

            // Right email, wrong password: record it on that account so it shows in their log
            var known = accounts.FirstOrDefault();
            if (isWeb && known != null)
                await _audit.AddAsync(known.Id, known.FullName, known.Role.ToLowerInvariant(),
                    "Login", "Failed sign-in (wrong password)", ClientIp, true);

            return Unauthorized(new { message = "Invalid email or password." });
        }

        if (!account.IsActive)
        {
            if (isWeb)
                await _audit.AddAsync(account.Id, account.FullName, account.Role.ToLowerInvariant(),
                    "Login", "Blocked sign-in (account is disabled)", ClientIp, true);

            return StatusCode(403, new { message = "This account is disabled." });
        }

        _throttle.Success(email, ClientIp);

        if (isWeb)
            await _audit.AddAsync(account.Id, account.FullName, account.Role.ToLowerInvariant(),
                "Login", "Signed in", ClientIp);

        return Ok(new AuthResponse
        {
            Role = account.Role,
            Id = account.Id,
            FullName = account.FullName,
            Email = account.Email,
            Token = _jwt.CreateToken(account.Id, account.FullName, account.Email, account.Role)
        });
    }

    // ================= FORGOT PASSWORD (staff and admin, from the web login) =================

    // Same keyed hash as the Change password code, so both flows use the same PasswordOtps table
    private byte[] HashCode(string role, int id, string code)
    {
        using var h = new HMACSHA256(_otpKey);
        return h.ComputeHash(Encoding.UTF8.GetBytes($"{role}:{id}:{code}"));
    }

    private static string TableFor(string role) => role == "admin" ? "Admins" : "Staff";

    // Disabled staff accounts are skipped, so they cannot use this to get back in
    private static async Task<ResetAccountRow?> FindAccountAsync(SqlConnection conn, string email)
    {
        var staff = await conn.QuerySingleOrDefaultAsync<ResetAccountRow>(@"
            SELECT TOP 1 Id, FullName, Email, CAST('staff' AS varchar(10)) AS Role, PasswordHash
            FROM Staff WHERE Email = @E AND IsActive = 1",
            new { E = email });
        if (staff is not null) return staff;

        return await conn.QuerySingleOrDefaultAsync<ResetAccountRow>(@"
            SELECT TOP 1 Id, FullName, Email, CAST('admin' AS varchar(10)) AS Role, PasswordHash
            FROM Admins WHERE Email = @E",
            new { E = email });
    }

    // The same answer whether or not the email has an account, so nobody can use this to find valid emails
    private IActionResult ForgotReply() => Ok(new
    {
        message = "If that email has an account, a 6-digit code is on its way.",
        expiresInSeconds = OtpMinutes * 60,
        resendInSeconds = ResendSeconds
    });

    // Step 1: email a 6-digit code
    [AllowAnonymous]
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest req)
    {
        var email = Normalize(req.Email ?? "");
        if (email.Length == 0 || email.Length > 150 || !EmailRx.IsMatch(email))
            return BadRequest(new { message = "Enter a valid email address." });

        using var conn = new SqlConnection(_connStr);
        await conn.OpenAsync();

        var account = await FindAccountAsync(conn, email);
        if (account is null) return ForgotReply();

        // Too soon or too many: quietly do nothing (the reply stays the same)
        var stats = await conn.QuerySingleAsync<PwOtpStats>(@"
            SELECT ISNULL(DATEDIFF(SECOND, MAX(CreatedAt), SYSDATETIME()), 9999) AS SecondsSinceLast,
                   COUNT(*) AS SentLastHour
            FROM PasswordOtps
            WHERE AccountRole = @Role AND AccountId = @Id
              AND CreatedAt >= DATEADD(HOUR, -1, SYSDATETIME())",
            new { Role = account.Role, Id = account.Id });

        if (stats.SecondsSinceLast < ResendSeconds || stats.SentLastHour >= MaxSendsPerHour)
            return ForgotReply();

        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

        int otpId;
        using (var tx = conn.BeginTransaction())
        {
            // Only the newest code can ever work
            await conn.ExecuteAsync(
                "UPDATE PasswordOtps SET UsedAt = SYSDATETIME() WHERE AccountRole = @Role AND AccountId = @Id AND UsedAt IS NULL",
                new { Role = account.Role, Id = account.Id }, tx);

            otpId = await conn.ExecuteScalarAsync<int>(@"
                INSERT INTO PasswordOtps (AccountRole, AccountId, CodeHash, ExpiresAt, IpAddress)
                OUTPUT INSERTED.Id
                VALUES (@Role, @Id, @Hash, DATEADD(MINUTE, @Min, SYSDATETIME()), @Ip)",
                new
                {
                    Role = account.Role,
                    Id = account.Id,
                    Hash = HashCode(account.Role, account.Id, code),
                    Min = OtpMinutes,
                    Ip = ClientIp
                }, tx);

            tx.Commit();
        }

        await _audit.AddAsync(account.Id, account.FullName, account.Role, "Login",
            "Requested a password reset code", ClientIp);

        // Sent in the background so the reply takes the same time for every email
        _ = SendCodeAsync(account, code, otpId);

        return ForgotReply();
    }

    private async Task SendCodeAsync(ResetAccountRow account, string code, int otpId)
    {
        try
        {
            var (html, text) = EmailTemplates.PasswordCode(account.FullName, code, OtpMinutes);
            await _email.SendAsync(account.Email, account.FullName,
                "Your Pension House verification code", html, text);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Could not send the password reset code to {Role}/{Id}", account.Role, account.Id);
            try
            {
                // The code never arrived, so remove it (the person can ask again straight away)
                using var conn = new SqlConnection(_connStr);
                await conn.ExecuteAsync("DELETE FROM PasswordOtps WHERE Id = @Id", new { Id = otpId });
            }
            catch
            {
            }
        }
    }

    // Step 2: check the code and set the new password
    [AllowAnonymous]
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest req)
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

        using var conn = new SqlConnection(_connStr);
        await conn.OpenAsync();

        var account = await FindAccountAsync(conn, email);
        if (account is null) return wrongCode;

        using var tx = conn.BeginTransaction();

        var row = await conn.QuerySingleOrDefaultAsync<PwOtpRow>(@"
            SELECT TOP 1 Id, CodeHash, Attempts
            FROM PasswordOtps WITH (UPDLOCK, ROWLOCK)
            WHERE AccountRole = @Role AND AccountId = @Id
              AND UsedAt IS NULL AND ExpiresAt > SYSDATETIME()
            ORDER BY CreatedAt DESC, Id DESC",
            new { Role = account.Role, Id = account.Id }, tx);

        if (row is null) return wrongCode;

        if (!CryptographicOperations.FixedTimeEquals(row.CodeHash, HashCode(account.Role, account.Id, otp)))
        {
            var attempts = row.Attempts + 1;
            var locked = attempts >= MaxAttempts;

            await conn.ExecuteAsync(
                "UPDATE PasswordOtps SET Attempts = @A, UsedAt = CASE WHEN @Locked = 1 THEN SYSDATETIME() ELSE UsedAt END WHERE Id = @OtpId",
                new { A = attempts, Locked = locked ? 1 : 0, OtpId = row.Id }, tx);
            tx.Commit();

            await _audit.AddAsync(account.Id, account.FullName, account.Role, "Login",
                "Failed password reset (wrong code)", ClientIp, true);

            return wrongCode;
        }

        // Checked only after the code is correct, so it can never be used to guess someone's current password
        if (BCrypt.Net.BCrypt.Verify(neu, account.PasswordHash))
            return BadRequest(new { message = "Choose a password that is different from your current one." });

        await conn.ExecuteAsync($"UPDATE {TableFor(account.Role)} SET PasswordHash = @Hash WHERE Id = @Id",
            new { Hash = newHash, Id = account.Id }, tx);
        await conn.ExecuteAsync(
            "UPDATE PasswordOtps SET UsedAt = SYSDATETIME() WHERE AccountRole = @Role AND AccountId = @Id AND UsedAt IS NULL",
            new { Role = account.Role, Id = account.Id }, tx);
        tx.Commit();

        await _audit.AddAsync(account.Id, account.FullName, account.Role, "Login",
            "Reset their password using Forgot password", ClientIp);

        // Best effort: the password is already changed, so a mail problem must not turn this into an error
        try
        {
            var (html, text) = EmailTemplates.PasswordChanged(account.FullName, DateTime.Now);
            await _email.SendAsync(account.Email, account.FullName,
                "Your Pension House password was changed", html, text);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Password reset but the notice email failed for {Role}/{Id}", account.Role, account.Id);
        }

        return Ok(new { message = "Password changed." });
    }
}