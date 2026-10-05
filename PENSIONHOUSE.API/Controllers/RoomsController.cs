using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/rooms")]
[Authorize]
public class RoomsController : ControllerBase
{
    private readonly IRoomRepository _repo;

    public RoomsController(IRoomRepository repo) => _repo = repo;

    private bool IsManager => User.IsInRole("staff") || User.IsInRole("admin");

    // Everyone logged in (mobile + web). Only staff/admin can include inactive rooms.
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] bool includeInactive = false)
        => Ok(await _repo.ListAsync(includeInactive && IsManager));

    // Rooms with no overlapping booking, e.g. /api/rooms/available?checkIn=2026-10-01T14:00:00&hours=3
    [HttpGet("available")]
    public async Task<IActionResult> Available([FromQuery] DateTime checkIn, [FromQuery] int hours)
    {
        if (hours <= 0)
            return BadRequest(new { message = "Hours must be greater than 0." });

        return Ok(await _repo.AvailableAsync(checkIn, hours));
    }

    // ---------------- ADMIN ONLY ----------------

    [HttpPost]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> Create(RoomRequest req)
    {
        var id = await _repo.CreateAsync(req);
        if (id == -1)
            return Conflict(new { message = "Room number already exists." });

        return Ok(new { message = "Room created.", id });
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> Update(int id, RoomRequest req)
    {
        var result = await _repo.UpdateAsync(id, req);
        if (result == -1) return Conflict(new { message = "Room number already exists." });
        if (result == 0) return NotFound(new { message = "Room not found." });

        return Ok(new { message = "Room updated." });
    }

    [HttpPatch("{id:int}/active")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> SetActive(int id, ActiveRequest req)
    {
        var rows = await _repo.SetActiveAsync(id, req.IsActive);
        if (rows == 0) return NotFound(new { message = "Room not found." });

        return Ok(new { message = "Room updated." });
    }

    [HttpPost("{id:int}/rates")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> AddRate(int id, RateRequest req)
    {
        var rateId = await _repo.AddRateAsync(id, req);
        if (rateId == -2) return NotFound(new { message = "Room not found." });

        return Ok(new { message = "Rate added.", id = rateId });
    }

    [HttpPut("rates/{rateId:int}")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> UpdateRate(int rateId, RateRequest req)
    {
        var rows = await _repo.UpdateRateAsync(rateId, req);
        if (rows == 0) return NotFound(new { message = "Rate not found." });

        return Ok(new { message = "Rate updated." });
    }

    [HttpDelete("rates/{rateId:int}")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> DeleteRate(int rateId)
    {
        var rows = await _repo.DeleteRateAsync(rateId);
        if (rows == 0) return NotFound(new { message = "Rate not found." });

        return Ok(new { message = "Rate removed." });
    }

    // ---------------- STAFF + ADMIN ----------------

    [HttpPatch("{id:int}/maintenance")]
    [Authorize(Roles = "staff,admin")]
    public async Task<IActionResult> SetMaintenance(int id, MaintenanceRequest req)
    {
        var result = await _repo.SetMaintenanceAsync(id, req.IsMaintenance);
        if (result == -1)
            return Conflict(new { message = "A guest is checked in. Complete the booking first." });
        if (result == 0)
            return NotFound(new { message = "Room not found." });

        return Ok(new { message = "Room updated." });
    }
}