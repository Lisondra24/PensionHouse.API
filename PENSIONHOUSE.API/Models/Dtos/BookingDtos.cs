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

    [StringLength(300)]
    public string? Reason { get; set; }                     // required when REJECTED
}

public class ExtendBookingRequest
{
    [Required, Range(1, 12)] public int ExtraHours { get; set; }
    public string PaymentMethod { get; set; } = "CASH";   // CASH or GCASH

    [StringLength(50)]
    public string? ReferenceCode { get; set; }            // required for GCASH when there is a charge
}

public class ExtendResult
{
    public int Code { get; set; }
    public DateTime? NewCheckOut { get; set; }
    public int? TotalHours { get; set; }
    public string? TierLabel { get; set; }
    public decimal? PreviousTotal { get; set; }
    public decimal? NewTotal { get; set; }
    public decimal? ExtraAmount { get; set; }
    public DateTime? NextCheckIn { get; set; }
}