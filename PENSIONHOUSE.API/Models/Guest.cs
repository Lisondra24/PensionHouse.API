public class Guest
{
    public int Id { get; set; }
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string? ContactNo { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public int TotalBookings { get; set; }
    public string? IdType { get; set; }
    public bool HasId { get; set; }
}