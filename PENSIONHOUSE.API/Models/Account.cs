public class Account
{
    public string Role { get; set; } = "";   // "user", "staff", "admin"
    public int Id { get; set; }
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public bool IsActive { get; set; }
}