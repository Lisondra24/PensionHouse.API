using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthRepository _repo;
    private readonly JwtService _jwt;

    public AuthController(IAuthRepository repo, JwtService jwt)
    {
        _repo = repo;
        _jwt = jwt;
    }

    private static string Normalize(string email) => email.Trim().ToLowerInvariant();

    // MOBILE ONLY: register a guest (role is always USER, never taken from the request)
    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest req)
    {
        var hash = BCrypt.Net.BCrypt.HashPassword(req.Password);

        var id = await _repo.CreateAccountAsync(
            AccountRole.User,
            req.FullName.Trim(),
            Normalize(req.Email),
            hash,
            req.ContactNo?.Trim());

        if (id == -1)
            return Conflict(new { message = "Email is already registered." });

        return Ok(new { message = "Registration successful.", id });
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
        var accounts = await _repo.GetLoginAccountsAsync(Normalize(req.Email), loginType);

        // The SP returns staff before admin, so this keeps that priority
        var account = accounts.FirstOrDefault(a =>
            BCrypt.Net.BCrypt.Verify(req.Password, a.PasswordHash));

        if (account == null)
            return Unauthorized(new { message = "Invalid email or password." });

        if (!account.IsActive)
            return StatusCode(403, new { message = "This account is disabled." });

        return Ok(new AuthResponse
        {
            Role = account.Role,
            Id = account.Id,
            FullName = account.FullName,
            Email = account.Email,
            Token = _jwt.CreateToken(account.Id, account.FullName, account.Email, account.Role)
        });
    }
}