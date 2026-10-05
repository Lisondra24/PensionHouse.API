using System.ComponentModel.DataAnnotations;

public class RoomRequest
{
    [Required, StringLength(20)]
    public string RoomNo { get; set; } = "";

    [Required, StringLength(100)]
    public string Title { get; set; } = "";

    [StringLength(1000)]
    public string? Description { get; set; }

    [Range(1, 20)]
    public int Capacity { get; set; } = 2;

    [StringLength(300)]
    public string? ImageUrl { get; set; }
}

public class RateRequest
{
    [Required, StringLength(50)]
    public string Label { get; set; } = "";

    [Range(1, 720)]
    public int Hours { get; set; }

    [Range(0.01, 1000000)]
    public decimal Price { get; set; }
}

public class ActiveRequest
{
    public bool IsActive { get; set; }
}

public class MaintenanceRequest
{
    public bool IsMaintenance { get; set; }
}