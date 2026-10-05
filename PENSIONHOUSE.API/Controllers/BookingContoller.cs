using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/bookings")]
[Authorize]
public class BookingsController : ControllerBase
{
    private static readonly string[] AllowedStatuses = { "CHECKED_IN", "COMPLETED", "CANCELLED" };

    private readonly IBookingRepository _repo;
    private readonly INotificationRepository _notifications;

    public BookingsController(IBookingRepository repo, INotificationRepository notifications)
    {
        _repo = repo;
        _notifications = notifications;
    }

    private int CurrentId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private IActionResult MapCreateResult(int id)
    {
        if (id > 0) return Ok(new { message = "Booking created.", id });

        return id switch
        {
            -1 => Conflict(new { message = "The room is not available for that time." }),
            -2 => BadRequest(new { message = "Invalid room or rate, or the room is under maintenance." }),
            -3 => Conflict(new { message = "That reference code was already used." }),
            -4 => BadRequest(new { message = "A GCash reference code is required." }),
            -5 => BadRequest(new { message = "Guest name is required." }),
            _ => StatusCode(500, new { message = "Could not create the booking." })
        };
    }

    // Tells staff a payment is waiting and tells admin about the new online booking.
    // Best-effort: a notification problem must never break the booking itself.
    private async Task NotifyOnlineBookingAsync(int bookingId, string referenceCode)
    {
        try
        {
            var b = (await _repo.ListAsync(null, null, bookingId, null)).FirstOrDefault();
            var who = string.IsNullOrWhiteSpace(b?.GuestName) ? "A guest" : b!.GuestName;
            var room = b == null ? "a room" : $"{b.RoomTitle} #{b.RoomNo}";

            await _notifications.CreateAsync("staff", "PAYMENT", "Payment awaiting approval",
                $"{who} booked {room}. GCash ref: {referenceCode}.");
            await _notifications.CreateAsync("admin", "BOOKING", "New online booking",
                $"{who} booked {room}.");
        }
        catch
        {
            // ignore
        }
    }

    // ---------------- MOBILE (role: user) ----------------

    [HttpPost]
    [Authorize(Roles = "user")]
    public async Task<IActionResult> Create(CreateBookingRequest req)
    {
        if (req.CheckIn < DateTime.Now.AddMinutes(-5))
            return BadRequest(new { message = "Check-in time cannot be in the past." });

        var refCode = req.ReferenceCode.Trim().ToUpperInvariant();

        var id = await _repo.CreateAsync(
            CurrentId, null, null, req.RoomId, req.RateId, req.CheckIn,
            "ONLINE", "GCASH", refCode);

        if (id > 0) await NotifyOnlineBookingAsync(id, refCode);

        return MapCreateResult(id);
    }

    [HttpGet("mine")]
    [Authorize(Roles = "user")]
    public async Task<IActionResult> Mine()
        => Ok(await _repo.ListAsync(CurrentId, null, null, null));

    [HttpPatch("{id:int}/cancel")]
    [Authorize(Roles = "user")]
    public async Task<IActionResult> Cancel(int id)
    {
        var rows = await _repo.UpdateStatusAsync(id, "CANCELLED", CurrentId);
        if (rows == 0)
            return BadRequest(new { message = "This booking can't be cancelled." });

        return Ok(new { message = "Booking cancelled." });
    }

    // ---------------- WEB ----------------

    // Walk-ins are created by staff only. Admin can view them through GET /api/bookings?source=WALKIN
    [HttpPost("walk-in")]
    [Authorize(Roles = "staff")]
    public async Task<IActionResult> WalkIn(WalkInRequest req)
    {
        var method = req.PaymentMethod.Trim().ToUpperInvariant();
        if (method != "CASH" && method != "GCASH")
            return BadRequest(new { message = "Payment method must be CASH or GCASH." });

        var id = await _repo.CreateAsync(
            null, req.GuestName.Trim(), req.GuestContact?.Trim(), req.RoomId, req.RateId,
            req.CheckIn ?? DateTime.Now, "WALKIN", method,
            req.ReferenceCode?.Trim().ToUpperInvariant(), req.IdType.Trim());

        return MapCreateResult(id);
    }

    [HttpGet]
    [Authorize(Roles = "staff,admin")]
    public async Task<IActionResult> List([FromQuery] string? status, [FromQuery] string? source)
        => Ok(await _repo.ListAsync(null, status, null, source));

    // Staff/admin can open any booking; a guest only their own
    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        var booking = (await _repo.ListAsync(null, null, id, null)).FirstOrDefault();
        if (booking == null) return NotFound();

        if (User.IsInRole("user") && booking.UserId != CurrentId)
            return Forbid();

        return Ok(booking);
    }

    [HttpPatch("{id:int}/status")]
    [Authorize(Roles = "staff,admin")]
    public async Task<IActionResult> UpdateStatus(int id, UpdateStatusRequest req)
    {
        var status = req.Status.Trim().ToUpperInvariant();
        if (!AllowedStatuses.Contains(status))
            return BadRequest(new { message = "Status must be CHECKED_IN, COMPLETED, or CANCELLED." });

        var rows = await _repo.UpdateStatusAsync(id, status, null);
        if (rows == 0)
            return BadRequest(new { message = "That status change isn't allowed for this booking." });

        return Ok(new { message = "Booking updated." });
    }
}