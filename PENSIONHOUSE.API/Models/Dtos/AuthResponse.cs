public class AuthResponse
{
    public string Role { get; set; } = "";   // "user", "staff", "admin"
    public int Id { get; set; }
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Token { get; set; } = "";
}