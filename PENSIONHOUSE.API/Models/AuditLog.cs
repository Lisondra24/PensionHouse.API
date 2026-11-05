public class AuditLog
{
    public int Id { get; set; }
    public int? UserId { get; set; }
    public string? UserName { get; set; }
    public string? Role { get; set; }
    public string Category { get; set; } = "";
    public string Details { get; set; } = "";
    public string? IpAddress { get; set; }
    public bool IsFailed { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class OnlineUser
{
    public int Id { get; set; }
    public string FullName { get; set; } = "";
    public string Role { get; set; } = "";
    public DateTime LastSeenAt { get; set; }
}

public class AuditStats
{
    public int OnlineNow { get; set; }
    public int ActionsToday { get; set; }
    public int FailedLogins { get; set; }
}