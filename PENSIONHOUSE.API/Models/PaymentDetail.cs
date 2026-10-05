public class PaymentDetail
{
    public int Id { get; set; }
    public int BookingId { get; set; }
    public string BookingCode { get; set; } = "";
    public string GuestName { get; set; } = "";
    public string RoomNo { get; set; } = "";
    public string RoomTitle { get; set; } = "";
    public DateTime CheckIn { get; set; }
    public DateTime CheckOut { get; set; }
    public string Method { get; set; } = "";
    public decimal Amount { get; set; }
    public string? ReferenceCode { get; set; }
    public string Status { get; set; } = "";
    public string? ReviewedByRole { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}