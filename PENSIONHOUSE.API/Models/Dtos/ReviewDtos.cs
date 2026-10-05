using System.ComponentModel.DataAnnotations;

public class CreateReviewRequest
{
    [Required] public int BookingId { get; set; }

    [Range(1, 5)]
    public int Rating { get; set; }

    [StringLength(1000)]
    public string? Comment { get; set; }      // optional
}

public class UpdateReviewRequest
{
    [Range(1, 5)]
    public int Rating { get; set; }

    [StringLength(1000)]
    public string? Comment { get; set; }
}

public class RoomReviewsResponse
{
    public ReviewSummary Summary { get; set; } = new();
    public List<Review> Reviews { get; set; } = new();
}