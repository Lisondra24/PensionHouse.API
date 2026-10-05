public class BookingDetail
{
    public int Id { get; set; }
    public string BookingCode { get; set; } = "";
    public int? UserId { get; set; }
    public string GuestName { get; set; } = "";
    public string? GuestContact { get; set; }
    public string? IdType { get; set; }
    public int RoomId { get; set; }
    public string RoomNo { get; set; } = "";
    public string RoomTitle { get; set; } = "";
    public int RateId { get; set; }
    public string RateLabel { get; set; } = "";
    public DateTime CheckIn { get; set; }
    public DateTime CheckOut { get; set; }
    public decimal TotalAmount { get; set; }
    public string Status { get; set; } = "";
    public string Source { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public int? PaymentId { get; set; }
    public string? PaymentMethod { get; set; }
    public string? PaymentStatus { get; set; }
    public string? ReferenceCode { get; set; }
}