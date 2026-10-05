using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/notifications")]
[Authorize(Roles = "staff,admin")]
public class NotificationsController : ControllerBase
{
    private readonly INotificationRepository _repo;

    public NotificationsController(INotificationRepository repo) => _repo = repo;

    private string Role => User.IsInRole("admin") ? "admin" : "staff";

    [HttpGet]
    public async Task<IActionResult> List()
        => Ok(new
        {
            unread = await _repo.UnreadCountAsync(Role),
            items = await _repo.ListAsync(Role)
        });

    [HttpPatch("{id:int}/read")]
    public async Task<IActionResult> MarkRead(int id)
    {
        await _repo.MarkReadAsync(id, Role);
        return Ok(new { message = "Marked as read." });
    }

    [HttpPatch("read-all")]
    public async Task<IActionResult> MarkAllRead()
    {
        await _repo.MarkAllReadAsync(Role);
        return Ok(new { message = "All marked as read." });
    }
}