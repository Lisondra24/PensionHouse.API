using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/audit-logs")]
[Authorize(Roles = "admin")]
public class AuditController : ControllerBase
{
    private readonly IAuditRepository _repo;

    public AuditController(IAuditRepository repo) => _repo = repo;

    // One call for the whole Audit logs tab: numbers, who is online, and the log list.
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] string? category, [FromQuery] string? search, [FromQuery] int limit = 100)
    {
        if (limit < 1) limit = 1;
        if (limit > 500) limit = 500;

        var stats = await _repo.StatsAsync();
        var online = await _repo.OnlineAsync();
        var logs = await _repo.ListAsync(category, search, limit);

        return Ok(new { stats, online, logs });
    }
}