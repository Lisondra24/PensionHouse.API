using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/guests")]
[Authorize(Roles = "staff,admin")]
public class GuestsController : ControllerBase
{
    private readonly IGuestRepository _repo;
    private readonly IAuditRepository _audit;

    public GuestsController(IGuestRepository repo, IAuditRepository audit)
    {
        _repo = repo;
        _audit = audit;
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? search)
        => Ok(await _repo.ListAsync(search, null));

    [HttpPatch("{id:int}/active")]
    public async Task<IActionResult> SetActive(int id, ActiveRequest req)
    {
        var rows = await _repo.SetActiveAsync(id, req.IsActive);
        if (rows == 0) return NotFound(new { message = "Guest not found." });

        var guest = (await _repo.ListAsync(null, id)).FirstOrDefault();
        var name = guest?.FullName ?? $"#{id}";
        await _audit.LogAsync(this, "Guest", (req.IsActive ? "Enabled guest " : "Disabled guest ") + name);

        return Ok(new { message = "Guest updated." });
    }

    // Admin only, even though the controller allows staff
    [Authorize(Roles = "admin")]
    [HttpGet("{id:int}/id/image")]
    public async Task<IActionResult> IdImage(int id, [FromServices] IWebHostEnvironment env)
    {
        var info = await _repo.GetIdAsync(id);
        if (info?.IdImageFile is null)
            return NotFound(new { message = "No ID on file." });

        // GetFileName blocks any path tricks
        var path = Path.Combine(env.ContentRootPath, "PrivateUploads", "ids", Path.GetFileName(info.IdImageFile));
        if (!System.IO.File.Exists(path))
            return NotFound(new { message = "ID file is missing." });

        var guest = (await _repo.ListAsync(null, id)).FirstOrDefault();
        await _audit.LogAsync(this, "Guest", "Viewed ID of guest " + (guest?.FullName ?? $"#{id}"));

        Response.Headers["Cache-Control"] = "private, no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        var type = path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? "image/png" : "image/jpeg";
        return PhysicalFile(path, type);
    }
}