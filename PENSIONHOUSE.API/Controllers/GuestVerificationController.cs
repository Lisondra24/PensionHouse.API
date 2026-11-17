using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

public record VerifyEmailRequest(string Email, string Otp);
public record ResendVerificationRequest(string Email);

/// Email verification for new guests (the mobile app).
[ApiController]
[AllowAnonymous]
[Route("api/guest-auth")]
public class GuestVerificationController : ControllerBase
{
    private static readonly Regex EmailRx = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);

    private readonly IAuthRepository _auth;
    private readonly EmailVerificationService _verify;

    public GuestVerificationController(IAuthRepository auth, EmailVerificationService verify)
    {
        _auth = auth;
        _verify = verify;
    }

    private static string Normalize(string email) => email.Trim().ToLowerInvariant();
    private string? ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString();

    private async Task<Account?> FindGuestAsync(string email)
    {
        var accounts = await _auth.GetLoginAccountsAsync(email, LoginType.Mobile);
        return accounts.FirstOrDefault(a =>
            string.Equals(a.Role, "user", StringComparison.OrdinalIgnoreCase) && a.IsActive);
    }

    // Step 2: check the code
    [HttpPost("verify-email")]
    public async Task<IActionResult> VerifyEmail(VerifyEmailRequest req)
    {
        var email = Normalize(req.Email ?? "");
        var otp = (req.Otp ?? "").Trim();

        if (!Regex.IsMatch(otp, "^[0-9]{6}$"))
            return BadRequest(new { message = "Enter the 6-digit code we emailed you." });

        // Every failure says the same thing, so this cannot be used to find valid emails
        var wrong = BadRequest(new { message = "That code isn’t correct or has expired. Check it, or request a new one." });

        var guest = await FindGuestAsync(email);
        if (guest is null) return wrong;
        if (await _verify.IsVerifiedAsync(guest.Id)) return wrong;
        if (!await _verify.VerifyAsync(guest.Id, otp)) return wrong;

        return Ok(new { message = "Email verified." });
    }

    // Send a new code
    [HttpPost("resend-verification")]
    public async Task<IActionResult> Resend(ResendVerificationRequest req)
    {
        var email = Normalize(req.Email ?? "");
        if (email.Length == 0 || email.Length > 150 || !EmailRx.IsMatch(email))
            return BadRequest(new { message = "Enter a valid email address." });

        var guest = await FindGuestAsync(email);
        if (guest is not null && !await _verify.IsVerifiedAsync(guest.Id))
            await _verify.SendCodeAsync(guest.Id, guest.Email, guest.FullName, ClientIp);

        // Same answer whether or not the email has an account
        return Ok(new
        {
            message = "If that email needs verifying, a 6-digit code is on its way.",
            expiresInSeconds = EmailVerificationService.OtpMinutes * 60,
            resendInSeconds = EmailVerificationService.ResendSeconds
        });
    }
}