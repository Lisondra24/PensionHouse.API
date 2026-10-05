public class Room
{
    public int Id { get; set; }
    public string RoomNo { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public int Capacity { get; set; }
    public string? ImageUrl { get; set; }
    public bool IsActive { get; set; }

    // Live status: AVAILABLE, OCCUPIED (a guest is checked in) or MAINTENANCE
    public bool IsMaintenance { get; set; }
    public string Status { get; set; } = "AVAILABLE";
    public string? CurrentGuest { get; set; }
    public DateTime? CurrentCheckIn { get; set; }
    public DateTime? CurrentCheckOut { get; set; }

    public List<RoomRate> Rates { get; set; } = new();
}

public class RoomRate
{
    public int Id { get; set; }
    public int RoomId { get; set; }
    public string Label { get; set; } = "";
    public int Hours { get; set; }
    public decimal Price { get; set; }
    public bool IsActive { get; set; }
}