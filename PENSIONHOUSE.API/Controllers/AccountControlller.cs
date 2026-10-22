using System.Security.Claims;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

public record UpdateProfileRequest(string FullName, string? ContactNo);
public record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public class MeResponse
{
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string? ContactNo { get; set; }
}

[ApiController]
[Authorize(Roles = "staff,admin")]
[Route("api/account")]
public class AccountController : ControllerBase
{
    private readonly string _connStr;

    public AccountController(IConfiguration config)
    {
        _connStr = config.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' not found.");
    }

    private SqlConnection Conn() => new SqlConnection(_connStr);

    // Who is calling: taken from the JWT, never from the request body
    private int UserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private bool IsAdmin => string.Equals(User.FindFirstValue(ClaimTypes.Role), "admin", StringComparison.OrdinalIgnoreCase);

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

    // Admin only. Staff passwords are reset by an admin (see StaffAccountsController).
    [Authorize(Roles = "admin")]
    [HttpPatch("password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest req)
    {
        if (string.IsNullOrEmpty(req.NewPassword) || req.NewPassword.Length < 8)
            return BadRequest(new { message = "The new password must be at least 8 characters." });
        if (req.NewPassword.Length > 72)   // BCrypt ignores anything past 72 characters
            return BadRequest(new { message = "The new password can be at most 72 characters." });

        using var conn = Conn();
        var hash = await conn.ExecuteScalarAsync<string?>(
            "SELECT PasswordHash FROM Admins WHERE Id = @Id", new { Id = UserId });
        if (hash is null)
            return NotFound(new { message = "Account not found." });

        // 400, not 401: the PHP site treats a 401 as an expired login and signs the user out
        if (!BCrypt.Net.BCrypt.Verify(req.CurrentPassword ?? "", hash))
            return BadRequest(new { message = "Your current password is incorrect." });

        await conn.ExecuteAsync(
            "UPDATE Admins SET PasswordHash = @Hash WHERE Id = @Id",
            new { Hash = BCrypt.Net.BCrypt.HashPassword(req.NewPassword), Id = UserId });

        return Ok(new { message = "Password changed." });
    }
}