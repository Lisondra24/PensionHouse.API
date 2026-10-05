using System.ComponentModel.DataAnnotations;

// Mobile guest booking (GCash)
public class CreateBookingRequest
{
    [Required] public int RoomId { get; set; }
    [Required] public int RateId { get; set; }
    [Required] public DateTime CheckIn { get; set; }

    [Required, StringLength(50)]
    public string ReferenceCode { get; set; } = "";   // GCash reference code
}

// Web walk-in booking (staff only)
public class WalkInRequest
{
    [Required, StringLength(100)]
    public string GuestName { get; set; } = "";

    [StringLength(20)]
    public string? GuestContact { get; set; }

    [Required, StringLength(30)]
    public string IdType { get; set; } = "";          // type of ID the guest showed

    [Required] public int RoomId { get; set; }
    [Required] public int RateId { get; set; }

    public DateTime? CheckIn { get; set; }            // empty = now
    public string PaymentMethod { get; set; } = "CASH";   // CASH or GCASH

    [StringLength(50)]
    public string? ReferenceCode { get; set; }        // required when GCASH
}

public class UpdateStatusRequest
{
    [Required] public string Status { get; set; } = "";
}

public class ReviewPaymentRequest
{
    [Required] public string Decision { get; set; } = "";   // APPROVED or REJECTED
}