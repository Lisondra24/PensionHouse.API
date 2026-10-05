public class Review
{
    public int Id { get; set; }
    public int BookingId { get; set; }
    public int UserId { get; set; }
    public int RoomId { get; set; }
    public string GuestName { get; set; } = "";
    public string RoomTitle { get; set; } = "";
    public int Rating { get; set; }
    public string? Comment { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

// Play Store style summary: average + how many reviews per star
public class ReviewSummary
{
    public int Total { get; set; }
    public decimal Average { get; set; }
    public int Star1 { get; set; }
    public int Star2 { get; set; }
    public int Star3 { get; set; }
    public int Star4 { get; set; }
    public int Star5 { get; set; }
}

// A finished booking the guest has not rated yet (mobile "Rate your stay" list)
public class ReviewableBooking
{
    public int BookingId { get; set; }
    public string BookingCode { get; set; } = "";
    public int RoomId { get; set; }
    public string RoomNo { get; set; } = "";
    public string RoomTitle { get; set; } = "";
    public DateTime CheckIn { get; set; }
    public DateTime CheckOut { get; set; }
}