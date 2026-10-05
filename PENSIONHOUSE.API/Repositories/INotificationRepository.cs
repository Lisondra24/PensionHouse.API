using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;

public interface INotificationRepository
{
    Task CreateAsync(string targetRole, string type, string title, string message);
    Task<List<Notification>> ListAsync(string targetRole);
    Task<int> UnreadCountAsync(string targetRole);
    Task<int> MarkReadAsync(int id, string targetRole);
    Task<int> MarkAllReadAsync(string targetRole);
}

public class NotificationRepository : INotificationRepository
{
    private const string SP = "SP_NORBERTODB_NOTIFICATIONS";
    private readonly string _connStr;

    public NotificationRepository(IConfiguration config)
    {
        _connStr = config.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' not found.");
    }

    private SqlConnection Conn() => new SqlConnection(_connStr);

    public async Task CreateAsync(string targetRole, string type, string title, string message)
    {
        using var conn = Conn();
        await conn.ExecuteAsync(SP, new
        {
            Action = "CREATE",
            TargetRole = targetRole,
            Type = type,
            Title = title,
            Message = message
        }, commandType: CommandType.StoredProcedure);
    }

    public async Task<List<Notification>> ListAsync(string targetRole)
    {
        using var conn = Conn();
        var rows = await conn.QueryAsync<Notification>(SP, new
        {
            Action = "LIST",
            TargetRole = targetRole
        }, commandType: CommandType.StoredProcedure);
        return rows.ToList();
    }

    public async Task<int> UnreadCountAsync(string targetRole)
    {
        using var conn = Conn();
        return await conn.ExecuteScalarAsync<int>(SP, new
        {
            Action = "COUNT_UNREAD",
            TargetRole = targetRole
        }, commandType: CommandType.StoredProcedure);
    }

    public async Task<int> MarkReadAsync(int id, string targetRole)
    {
        using var conn = Conn();
        return await conn.ExecuteScalarAsync<int>(SP, new
        {
            Action = "MARK_READ",
            Id = id,
            TargetRole = targetRole
        }, commandType: CommandType.StoredProcedure);
    }

    public async Task<int> MarkAllReadAsync(string targetRole)
    {
        using var conn = Conn();
        return await conn.ExecuteScalarAsync<int>(SP, new
        {
            Action = "MARK_ALL_READ",
            TargetRole = targetRole
        }, commandType: CommandType.StoredProcedure);
    }
}