using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/guests")]
[Authorize(Roles = "staff,admin")]
public class GuestsController : ControllerBase
{
    private readonly IGuestRepository _repo;

    public GuestsController(IGuestRepository repo) => _repo = repo;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? search)
        => Ok(await _repo.ListAsync(search, null));

    [HttpPatch("{id:int}/active")]
    public async Task<IActionResult> SetActive(int id, ActiveRequest req)
    {
        var rows = await _repo.SetActiveAsync(id, req.IsActive);
        if (rows == 0) return NotFound(new { message = "Guest not found." });

        return Ok(new { message = "Guest updated." });
    }
}