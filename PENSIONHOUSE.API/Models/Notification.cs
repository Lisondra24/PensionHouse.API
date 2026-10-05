public class Notification
{
    public int Id { get; set; }
    public string TargetRole { get; set; } = "";
    public string Type { get; set; } = "";      // BOOKING, PAYMENT, REVIEW
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; }
}