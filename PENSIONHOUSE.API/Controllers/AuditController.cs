using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/audit-logs")]
public class AuditController : ControllerBase
{
    private readonly IAuditRepository _repo;

    public AuditController(IAuditRepository repo) => _repo = repo;

    // ADMIN: one call for the whole Audit logs tab: numbers, who is online, and the log list.
    [HttpGet]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> Get([FromQuery] string? category, [FromQuery] string? search, [FromQuery] int limit = 100)
    {
        if (limit < 1) limit = 1;
        if (limit > 500) limit = 500;

        var stats = await _repo.StatsAsync();
        var online = await _repo.OnlineAsync();
        var logs = await _repo.ListAsync(category, search, limit);

        return Ok(new { stats, online, logs });
    }

    // STAFF (and admin): only the caller's own activity. The id and role come from the token, never from the request.
    [HttpGet("mine")]
    [Authorize(Roles = "staff,admin")]
    public async Task<IActionResult> Mine([FromQuery] int limit = 200)
    {
        if (limit < 1) limit = 1;
        if (limit > 500) limit = 500;

        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return Unauthorized();

        var role = User.FindFirstValue(ClaimTypes.Role);
        var all = await _repo.ListForUserAsync(userId, role);

        var today = DateTime.Today;
        var stats = new
        {
            actionsToday = all.Count(l => l.CreatedAt >= today),
            last7Days = all.Count(l => l.CreatedAt >= today.AddDays(-6)),
            failedAttempts = all.Count(l => l.IsFailed)
        };

        return Ok(new { stats, logs = all.Take(limit).ToList() });
    }
}