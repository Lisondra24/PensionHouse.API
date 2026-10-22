using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Text.RegularExpressions;

public record CreateStaffAccountRequest(string FullName, string Email, string? ContactNo, string Password);
public record StaffActiveFlagRequest(bool IsActive);
public record StaffNewPasswordRequest(string NewPassword);

public class StaffAccountRow
{
    public int Id { get; set; }
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string? ContactNo { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}

[ApiController]
[Authorize(Roles = "admin")]
[Route("api/staff-accounts")]
public class StaffAccountsController : ControllerBase
{
    private readonly string _connStr;

    public StaffAccountsController(IConfiguration config)
    {
        _connStr = config.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' not found.");
    }

    private SqlConnection Conn() => new SqlConnection(_connStr);

    [HttpGet]
    public async Task<IActionResult> List()
    {
        using var conn = Conn();
        var rows = await conn.QueryAsync<StaffAccountRow>(
            "SELECT Id, FullName, Email, ContactNo, IsActive, CreatedAt FROM Staff ORDER BY FullName");
        return Ok(rows);
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateStaffAccountRequest req)
    {
        var name = req.FullName?.Trim() ?? "";
        var email = req.Email?.Trim().ToLowerInvariant() ?? "";
        var contact = string.IsNullOrWhiteSpace(req.ContactNo) ? null : req.ContactNo.Trim();

        if (name.Length == 0 || name.Length > 100)
            return BadRequest(new { message = "Enter the staff member's full name." });
        if (email.Length == 0 || email.Length > 150 || !Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
            return BadRequest(new { message = "Enter a valid email address." });
        if (contact is not null && !Regex.IsMatch(contact, @"^[0-9+\-\s]{7,20}$"))
            return BadRequest(new { message = "Enter a valid mobile number, or leave it blank." });
        if (string.IsNullOrEmpty(req.Password) || req.Password.Length < 8 || req.Password.Length > 72)
            return BadRequest(new { message = "The password must be 8 to 72 characters." });

        using var conn = Conn();

        // The web login looks in both tables, so the email must be free in both
        var taken = await conn.ExecuteScalarAsync<int>(
            "SELECT CASE WHEN EXISTS (SELECT 1 FROM Staff WHERE Email = @E) OR EXISTS (SELECT 1 FROM Admins WHERE Email = @E) THEN 1 ELSE 0 END",
            new { E = email });
        if (taken == 1)
            return Conflict(new { message = "That email is already used by another staff or admin account." });

        var id = await conn.ExecuteScalarAsync<int>(@"
            INSERT INTO Staff (FullName, Email, PasswordHash, ContactNo, IsActive, CreatedAt)
            OUTPUT INSERTED.Id
            VALUES (@Name, @Email, @Hash, @Contact, 1, SYSDATETIME())",
            new { Name = name, Email = email, Hash = BCrypt.Net.BCrypt.HashPassword(req.Password), Contact = contact });

        return Ok(new { message = "Staff account created.", id });
    }

    [HttpPatch("{id:int}/active")]
    public async Task<IActionResult> SetActive(int id, StaffActiveFlagRequest req)
    {
        using var conn = Conn();
        var rows = await conn.ExecuteAsync("UPDATE Staff SET IsActive = @A WHERE Id = @Id",
            new { A = req.IsActive, Id = id });
        return rows == 0 ? NotFound(new { message = "Staff account not found." }) : Ok(new { message = "Staff account updated." });
    }

    [HttpPatch("{id:int}/password")]
    public async Task<IActionResult> ResetPassword(int id, StaffNewPasswordRequest req)
    {
        if (string.IsNullOrEmpty(req.NewPassword) || req.NewPassword.Length < 8 || req.NewPassword.Length > 72)
            return BadRequest(new { message = "The password must be 8 to 72 characters." });

        using var conn = Conn();
        var rows = await conn.ExecuteAsync("UPDATE Staff SET PasswordHash = @H WHERE Id = @Id",
            new { H = BCrypt.Net.BCrypt.HashPassword(req.NewPassword), Id = id });
        return rows == 0 ? NotFound(new { message = "Staff account not found." }) : Ok(new { message = "Password reset." });
    }
}