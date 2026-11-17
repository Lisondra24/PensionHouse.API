using System.Net;

public static class EmailTemplates
{
    private const string Teal = "#123f3a";

    private static string FirstName(string? fullName)
    {
        var n = (fullName ?? "").Trim();
        var i = n.IndexOf(' ');
        return i > 0 ? n[..i] : (n.Length > 0 ? n : "there");
    }

    private static string Shell(string preheader, string heading, string inner) =>
        "<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\">"
      + "<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>Pension House</title></head>"
      + "<body style=\"margin:0;padding:0;background:#f3f6f5;\">"
      + $"<div style=\"display:none;max-height:0;overflow:hidden;opacity:0;\">{WebUtility.HtmlEncode(preheader)}</div>"
      + "<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"background:#f3f6f5;padding:32px 12px;\"><tr><td align=\"center\">"
      + "<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"max-width:520px;background:#ffffff;border-radius:16px;overflow:hidden;border:1px solid #e3e9e7;font-family:'Segoe UI',Helvetica,Arial,sans-serif;color:#0f3d3a;\">"
      + $"<tr><td style=\"background:{Teal};padding:22px 28px;color:#ffffff;font-size:18px;font-weight:600;\">Pension House</td></tr>"
      + $"<tr><td style=\"padding:30px 28px 8px 28px;\"><h1 style=\"margin:0 0 12px 0;font-size:20px;line-height:1.3;color:{Teal};\">{heading}</h1>{inner}</td></tr>"
      + "<tr><td style=\"padding:20px 28px 28px 28px;font-size:12px;line-height:1.6;color:#6b7f7a;border-top:1px solid #eef1f0;\">"
      + "This is an automated message from the Pension House admin panel. Please do not reply to this email.</td></tr>"
      + "</table></td></tr></table></body></html>";

    public static (string Html, string Text) PasswordCode(string fullName, string code, int minutes)
    {
        var name = WebUtility.HtmlEncode(FirstName(fullName));

        var inner =
            $"<p style=\"margin:0 0 18px 0;font-size:14px;line-height:1.7;\">Hi {name}, use this code to confirm your password change.</p>"
          + $"<div style=\"margin:0 0 18px 0;padding:18px 12px 18px 22px;background:#f3f6f5;border:1px solid #dfe8e5;border-radius:12px;text-align:center;font-family:Consolas,Menlo,monospace;font-size:34px;font-weight:700;letter-spacing:10px;color:{Teal};\">{code}</div>"
          + $"<p style=\"margin:0 0 10px 0;font-size:13px;line-height:1.7;\">This code expires in <strong>{minutes} minutes</strong> and can only be used once.</p>"
          + "<p style=\"margin:0 0 22px 0;font-size:13px;line-height:1.7;color:#5f7772;\">Never share this code with anyone. Pension House staff will never ask you for it.</p>"
          + "<p style=\"margin:0 0 22px 0;padding:12px 14px;background:#fff7f4;border:1px solid #f3d3c9;border-radius:10px;font-size:12.5px;line-height:1.6;color:#7a3a2a;\">Didn’t request this? You can ignore this email. Your password has not been changed.</p>";

        var text =
            $"Hi {FirstName(fullName)},\n\n"
          + $"Your Pension House verification code is: {code}\n\n"
          + $"It expires in {minutes} minutes and can only be used once.\n"
          + "Never share this code with anyone.\n\n"
          + "Didn’t request this? Ignore this email. Your password has not been changed.";

        return (Shell($"Your Pension House verification code is {code}", "Verification code", inner), text);
    }

    public static (string Html, string Text) PasswordChanged(string fullName, DateTime when)
    {
        var name = WebUtility.HtmlEncode(FirstName(fullName));
        var stamp = when.ToString("MMM d, yyyy 'at' h:mm tt");

        var inner =
            $"<p style=\"margin:0 0 14px 0;font-size:14px;line-height:1.7;\">Hi {name}, the password for your Pension House account was changed on <strong>{stamp}</strong>.</p>"
          + "<p style=\"margin:0 0 22px 0;padding:12px 14px;background:#fff7f4;border:1px solid #f3d3c9;border-radius:10px;font-size:12.5px;line-height:1.6;color:#7a3a2a;\">If this wasn’t you, tell the administrator right away so your account can be secured.</p>";

        var text =
            $"Hi {FirstName(fullName)},\n\n"
          + $"The password for your Pension House account was changed on {stamp}.\n\n"
          + "If this wasn’t you, tell the administrator right away so your account can be secured.";

        return (Shell("Your Pension House password was changed", "Password changed", inner), text);
    }

    public static (string Html, string Text) VerifyEmailCode(string fullName, string code, int minutes)
    {
        var name = WebUtility.HtmlEncode(FirstName(fullName));

        var inner =
            $"<p style=\"margin:0 0 18px 0;font-size:14px;line-height:1.7;\">Hi {name}, welcome to Pension House! Use this code to verify your email address.</p>"
          + $"<div style=\"margin:0 0 18px 0;padding:18px 12px 18px 22px;background:#f3f6f5;border:1px solid #dfe8e5;border-radius:12px;text-align:center;font-family:Consolas,Menlo,monospace;font-size:34px;font-weight:700;letter-spacing:10px;color:{Teal};\">{code}</div>"
          + $"<p style=\"margin:0 0 10px 0;font-size:13px;line-height:1.7;\">This code expires in <strong>{minutes} minutes</strong> and can only be used once.</p>"
          + "<p style=\"margin:0 0 22px 0;font-size:13px;line-height:1.7;color:#5f7772;\">Never share this code with anyone.</p>"
          + "<p style=\"margin:0 0 22px 0;padding:12px 14px;background:#fff7f4;border:1px solid #f3d3c9;border-radius:10px;font-size:12.5px;line-height:1.6;color:#7a3a2a;\">Didn’t create an account? You can ignore this email.</p>";

        var text =
            $"Hi {FirstName(fullName)},\n\n"
          + $"Your Pension House email verification code is: {code}\n\n"
          + $"It expires in {minutes} minutes and can only be used once.\n"
          + "Never share this code with anyone.\n\n"
          + "Didn’t create an account? Ignore this email.";

        return (Shell($"Your Pension House verification code is {code}", "Verify your email", inner), text);
    }

    public static (string Html, string Text) PaymentRejected(string fullName, string bookingCode, string reason)
    {
        var name = WebUtility.HtmlEncode(FirstName(fullName));
        var why = WebUtility.HtmlEncode(reason);
        var code = WebUtility.HtmlEncode(bookingCode);

        var inner =
            $"<p style=\"margin:0 0 14px 0;font-size:14px;line-height:1.7;\">Hi {name}, we could not approve your GCash payment for booking <strong>{code}</strong>, so the booking was cancelled.</p>"
          + "<p style=\"margin:0 0 6px 0;font-size:13px;font-weight:600;\">Reason</p>"
          + $"<p style=\"margin:0 0 18px 0;padding:12px 14px;background:#fff7f4;border:1px solid #f3d3c9;border-radius:10px;font-size:13.5px;line-height:1.6;color:#7a3a2a;\">{why}</p>"
          + "<p style=\"margin:0 0 22px 0;font-size:13px;line-height:1.7;color:#5f7772;\">You are welcome to book again with the correct payment details, or contact the front desk if you think this was a mistake.</p>";

        var text =
            $"Hi {FirstName(fullName)},\n\n"
          + $"We could not approve your GCash payment for booking {bookingCode}, so the booking was cancelled.\n\n"
          + $"Reason: {reason}\n\n"
          + "You are welcome to book again with the correct payment details, or contact the front desk if you think this was a mistake.";

        return (Shell("Your payment could not be approved", "Payment rejected", inner), text);
    }
}