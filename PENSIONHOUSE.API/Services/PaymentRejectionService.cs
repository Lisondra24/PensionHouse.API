using Dapper;
using Microsoft.Data.SqlClient;

public class RejectionRow
{
    public int Key { get; set; }
    public string Reason { get; set; } = "";
}

public class GuestContact
{
    public string Email { get; set; } = "";
    public string FullName { get; set; } = "";
}

/// Keeps the reason an admin or staff member gave for rejecting a payment,
/// and tells the guest by email.
public class PaymentRejectionService
{
    private const string GuestTable = "Users";

    private readonly string _connStr;
    private readonly IBookingRepository _bookings;
    private readonly IEmailSender _email;
    private readonly ILogger<PaymentRejectionService> _log;

    public PaymentRejectionService(IConfiguration config, IBookingRepository bookings,
                                   IEmailSender email, ILogger<PaymentRejectionService> log)
    {
        _bookings = bookings;
        _email = email;
        _log = log;
        _connStr = config.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' not found.");
    }

    public async Task SaveAsync(PaymentDetail pay, string reason, string reviewerRole)
    {
        using var conn = new SqlConnection(_connStr);
        await conn.ExecuteAsync(@"
            MERGE dbo.PaymentRejections AS t
            USING (SELECT @PaymentId AS PaymentId) AS s ON t.PaymentId = s.PaymentId
            WHEN MATCHED THEN
                UPDATE SET Reason = @Reason, RejectedByRole = @Role, CreatedAt = SYSDATETIME()
            WHEN NOT MATCHED THEN
                INSERT (PaymentId, BookingId, Reason, RejectedByRole)
                VALUES (@PaymentId, @BookingId, @Reason, @Role);",
            new { PaymentId = pay.Id, BookingId = pay.BookingId, Reason = reason, Role = reviewerRole });
    }

    // Used when the review itself did not go through
    public async Task RemoveAsync(int paymentId)
    {
        using var conn = new SqlConnection(_connStr);
        await conn.ExecuteAsync("DELETE FROM dbo.PaymentRejections WHERE PaymentId = @Id", new { Id = paymentId });
    }

    /// Emails the guest. Best effort: a mail problem must never undo the rejection.
    public async Task NotifyGuestAsync(PaymentDetail pay, string reason)
    {
        try
        {
            var booking = (await _bookings.ListAsync(null, null, pay.BookingId, null)).FirstOrDefault();
            if (booking?.UserId is not int userId) return;   // walk-in: no app account

            using var conn = new SqlConnection(_connStr);
            var guest = await conn.QuerySingleOrDefaultAsync<GuestContact>(
                $"SELECT Email, FullName FROM {GuestTable} WHERE Id = @Id", new { Id = userId });
            if (guest is null || string.IsNullOrWhiteSpace(guest.Email)) return;

            var (html, text) = EmailTemplates.PaymentRejected(guest.FullName, pay.BookingCode, reason);
            _ = SendSafeAsync(guest, html, text);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Could not prepare the rejection email for payment {Id}", pay.Id);
        }
    }

    private async Task SendSafeAsync(GuestContact guest, string html, string text)
    {
        try
        {
            await _email.SendAsync(guest.Email, guest.FullName,
                "Your Pension House payment was rejected", html, text);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Rejection email could not be sent");
        }
    }

    // For the admin/staff payments list
    public async Task FillPaymentsAsync(IEnumerable<PaymentDetail> payments)
    {
        var list = payments.Where(p => p.Status == "REJECTED").ToList();
        if (list.Count == 0) return;

        using var conn = new SqlConnection(_connStr);
        var rows = await conn.QueryAsync<RejectionRow>(
            "SELECT PaymentId AS [Key], Reason FROM dbo.PaymentRejections WHERE PaymentId IN @Ids",
            new { Ids = list.Select(p => p.Id).ToList() });
        var map = rows.ToDictionary(r => r.Key, r => r.Reason);

        foreach (var p in list)
            if (map.TryGetValue(p.Id, out var why)) p.RejectReason = why;
    }

    // For the guest's bookings (latest rejection of each booking)
    public async Task FillBookingsAsync(IEnumerable<BookingDetail> bookings)
    {
        var list = bookings.Where(b => b.PaymentStatus == "REJECTED").ToList();
        if (list.Count == 0) return;

        using var conn = new SqlConnection(_connStr);
        var rows = await conn.QueryAsync<RejectionRow>(@"
            SELECT BookingId AS [Key], Reason FROM (
                SELECT BookingId, Reason,
                       ROW_NUMBER() OVER (PARTITION BY BookingId ORDER BY CreatedAt DESC, Id DESC) AS rn
                FROM dbo.PaymentRejections WHERE BookingId IN @Ids) x
            WHERE rn = 1",
            new { Ids = list.Select(b => b.Id).ToList() });
        var map = rows.ToDictionary(r => r.Key, r => r.Reason);

        foreach (var b in list)
            if (map.TryGetValue(b.Id, out var why)) b.RejectReason = why;
    }
}