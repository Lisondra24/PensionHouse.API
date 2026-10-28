using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private const long MaxIdBytes = 5 * 1024 * 1024;   // 5 MB

    private readonly IAuthRepository _repo;
    private readonly JwtService _jwt;
    private readonly IWebHostEnvironment _env;

    public AuthController(IAuthRepository repo, JwtService jwt, IWebHostEnvironment env)
    {
        _repo = repo;
        _jwt = jwt;
        _env = env;
    }

    private static string Normalize(string email) => email.Trim().ToLowerInvariant();

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