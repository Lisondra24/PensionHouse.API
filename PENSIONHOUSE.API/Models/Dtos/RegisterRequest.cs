using System.ComponentModel.DataAnnotations;

public class RegisterRequest
{
    [Required, StringLength(100)]
    public string FullName { get; set; } = "";

    [Required, EmailAddress, StringLength(150)]
    public string Email { get; set; } = "";

    [Required, MinLength(6), StringLength(100)]
    public string Password { get; set; } = "";

    [StringLength(20)]
    public string? ContactNo { get; set; }

    [Required, StringLength(50)]
    public string IdType { get; set; } = "";

    [Required]
    public IFormFile IdImage { get; set; } = null!;
}