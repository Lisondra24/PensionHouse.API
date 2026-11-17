using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

public class EmailSettings
{
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public bool UseStartTls { get; set; } = true;     // true = port 587, false = port 465 (SSL)
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string FromEmail { get; set; } = "";
    public string FromName { get; set; } = "Pension House";
}

public interface IEmailSender
{
    Task SendAsync(string toEmail, string toName, string subject, string html, string text, CancellationToken ct = default);
}

public class EmailSender : IEmailSender
{
    private readonly EmailSettings _s;

    public EmailSender(IOptions<EmailSettings> options) => _s = options.Value;

    public async Task SendAsync(string toEmail, string toName, string subject, string html, string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_s.Host) || string.IsNullOrWhiteSpace(_s.Username) || string.IsNullOrWhiteSpace(_s.Password))
            throw new InvalidOperationException("Email is not configured. Fill in the Email section of appsettings.");

        var from = string.IsNullOrWhiteSpace(_s.FromEmail) ? _s.Username : _s.FromEmail;

        var msg = new MimeMessage();
        msg.From.Add(new MailboxAddress(_s.FromName, from));
        msg.To.Add(new MailboxAddress(toName, toEmail));
        msg.Subject = subject;
        msg.Body = new BodyBuilder { HtmlBody = html, TextBody = text }.ToMessageBody();

        using var client = new SmtpClient { Timeout = 15000 };
        await client.ConnectAsync(_s.Host, _s.Port,
            _s.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.SslOnConnect, ct);
        await client.AuthenticateAsync(_s.Username, _s.Password, ct);
        await client.SendAsync(msg, ct);
        await client.DisconnectAsync(true, ct);
    }
}