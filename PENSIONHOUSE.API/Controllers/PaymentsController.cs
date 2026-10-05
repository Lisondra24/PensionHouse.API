using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/payments")]
[Authorize(Roles = "staff,admin")]
public class PaymentsController : ControllerBase
{
    private readonly IPaymentRepository _repo;

    public PaymentsController(IPaymentRepository repo) => _repo = repo;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? status)
        => Ok(await _repo.ListAsync(status));

    [HttpPatch("{id:int}/review")]
    public async Task<IActionResult> Review(int id, ReviewPaymentRequest req)
    {
        var decision = req.Decision.Trim().ToUpperInvariant();
        if (decision != "APPROVED" && decision != "REJECTED")
            return BadRequest(new { message = "Decision must be APPROVED or REJECTED." });

        var role = User.FindFirstValue(ClaimTypes.Role)!;
        var reviewerId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var result = await _repo.ReviewAsync(id, decision, role, reviewerId);
        if (result != 1)
            return BadRequest(new { message = "Payment not found or already reviewed." });

        return Ok(new { message = $"Payment {decision.ToLower()}." });
    }
}