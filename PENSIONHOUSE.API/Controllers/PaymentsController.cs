using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/payments")]
[Authorize(Roles = "staff,admin")]
public class PaymentsController : ControllerBase
{
    private readonly IPaymentRepository _repo;
    private readonly PaymentRejectionService _rejections;

    public PaymentsController(IPaymentRepository repo, PaymentRejectionService rejections)
    {
        _repo = repo;
        _rejections = rejections;
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? status)
    {
        var list = await _repo.ListAsync(status);
        await _rejections.FillPaymentsAsync(list);   // adds the reason to rejected payments
        return Ok(list);
    }

    [HttpPatch("{id:int}/review")]
    public async Task<IActionResult> Review(int id, ReviewPaymentRequest req)
    {
        var decision = req.Decision.Trim().ToUpperInvariant();
        if (decision != "APPROVED" && decision != "REJECTED")
            return BadRequest(new { message = "Decision must be APPROVED or REJECTED." });

        var role = User.FindFirstValue(ClaimTypes.Role)!;
        var reviewerId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var reason = (req.Reason ?? "").Trim();
        PaymentDetail? pay = null;

        if (decision == "REJECTED")
        {
            if (reason.Length < 5)
                return BadRequest(new { message = "Please write why the payment is rejected (at least 5 characters)." });
            if (reason.Length > 300)
                return BadRequest(new { message = "The reason must be 300 characters or less." });

            pay = (await _repo.ListAsync(null)).FirstOrDefault(p => p.Id == id);
            if (pay is null)
                return BadRequest(new { message = "Payment not found or already reviewed." });

            // Saved first, so the reason can never be lost after the payment is rejected
            await _rejections.SaveAsync(pay, reason, role);
        }

        var result = await _repo.ReviewAsync(id, decision, role, reviewerId);
        if (result != 1)
        {
            if (pay is not null) await _rejections.RemoveAsync(id);
            return BadRequest(new { message = "Payment not found or already reviewed." });
        }

        if (pay is not null)
            await _rejections.NotifyGuestAsync(pay, reason);

        return Ok(new { message = $"Payment {decision.ToLower()}." });
    }
}