using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/reviews")]
[Authorize]
public class ReviewsController : ControllerBase
{
    private readonly IReviewRepository _reviews;
    private readonly IBookingRepository _bookings;
    private readonly INotificationRepository _notifications;

    public ReviewsController(IReviewRepository reviews, IBookingRepository bookings,
                             INotificationRepository notifications)
    {
        _reviews = reviews;
        _bookings = bookings;
        _notifications = notifications;
    }

    private int CurrentId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    // A guest can rate once the stay is over: COMPLETED, or CHECKED_IN and past the check-out time.
    private static bool CanReview(BookingDetail b)
        => b.Status == "COMPLETED" || (b.Status == "CHECKED_IN" && b.CheckOut <= DateTime.Now);

    private static string? CleanComment(string? c)
        => string.IsNullOrWhiteSpace(c) ? null : c.Trim();

    // ---------------- MOBILE (role: user) ----------------

    // Bookings the guest can rate right now and hasn't rated yet
    [HttpGet("pending")]
    [Authorize(Roles = "user")]
    public async Task<IActionResult> Pending()
    {
        var mine = await _bookings.ListAsync(CurrentId, null, null, null);
        var reviewed = (await _reviews.ReviewedBookingIdsAsync(CurrentId)).ToHashSet();

        var list = mine
            .Where(b => CanReview(b) && !reviewed.Contains(b.Id))
            .OrderByDescending(b => b.CheckOut)
            .Select(b => new ReviewableBooking
            {
                BookingId = b.Id,
                BookingCode = b.BookingCode,
                RoomId = b.RoomId,
                RoomNo = b.RoomNo,
                RoomTitle = b.RoomTitle,
                CheckIn = b.CheckIn,
                CheckOut = b.CheckOut
            });

        return Ok(list);
    }

    [HttpPost]
    [Authorize(Roles = "user")]
    public async Task<IActionResult> Create(CreateReviewRequest req)
    {
        var booking = (await _bookings.ListAsync(null, null, req.BookingId, null)).FirstOrDefault();
        if (booking == null || booking.UserId != CurrentId)
            return NotFound(new { message = "Booking not found." });

        if (!CanReview(booking))
            return BadRequest(new { message = "You can rate your stay after your booking time ends." });

        var guestName = string.IsNullOrWhiteSpace(booking.GuestName) ? "Guest" : booking.GuestName;

        var id = await _reviews.CreateAsync(booking.Id, CurrentId, booking.RoomId, guestName,
                                            booking.RoomTitle, req.Rating, CleanComment(req.Comment));

        if (id == -1) return Conflict(new { message = "You already reviewed this booking." });
        if (id <= 0) return StatusCode(500, new { message = "Could not save your review." });

        try
        {
            await _notifications.CreateAsync("admin", "REVIEW", "New review",
                $"{guestName} rated {booking.RoomTitle} {req.Rating}/5.");
        }
        catch
        {
            // notifications are best-effort; the review is already saved
        }

        return Ok(new { message = "Thanks for your review!", id });
    }

    // Guest edits their own review (like the Play Store)
    [HttpPut("{id:int}")]
    [Authorize(Roles = "user")]
    public async Task<IActionResult> Update(int id, UpdateReviewRequest req)
    {
        var rows = await _reviews.UpdateAsync(id, CurrentId, req.Rating, CleanComment(req.Comment));
        if (rows == 0) return NotFound(new { message = "Review not found." });

        return Ok(new { message = "Review updated." });
    }

    [HttpGet("mine")]
    [Authorize(Roles = "user")]
    public async Task<IActionResult> Mine()
        => Ok(await _reviews.ListAsync(null, CurrentId, null, null));

    // Rating summary + reviews of one room (shown on the mobile room page, Play Store style)
    [HttpGet("room/{roomId:int}")]
    public async Task<IActionResult> ForRoom(int roomId)
        => Ok(new RoomReviewsResponse
        {
            Summary = await _reviews.SummaryAsync(roomId),
            Reviews = await _reviews.ListAsync(null, null, roomId, null)
        });

    // ---------------- WEB ----------------

    [HttpGet]
    [Authorize(Roles = "staff,admin")]
    public async Task<IActionResult> List([FromQuery] int? rating, [FromQuery] int? roomId)
        => Ok(await _reviews.ListAsync(null, null, roomId, rating));

    [HttpGet("summary")]
    [Authorize(Roles = "staff,admin")]
    public async Task<IActionResult> Summary([FromQuery] int? roomId)
        => Ok(await _reviews.SummaryAsync(roomId));
}