using System.Data;
using System.Security.Claims;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

public interface IAuditRepository
{
    Task AddAsync(int? userId, string? userName, string? role, string category, string details, string? ip, bool isFailed = false);
    Task TouchAsync(int userId, string role);
    Task<List<AuditLog>> ListAsync(string? category, string? search, int limit);
    Task<List<OnlineUser>> OnlineAsync();
    Task<AuditStats> StatsAsync();
}

public class AuditRepository : IAuditRepository
{
    private const string SP = "SP_NORBERTODB_AUDIT";
    private readonly string _connStr;

    public AuditRepository(IConfiguration config)
    {
        _connStr = config.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' not found.");
    }

    private SqlConnection Conn() => new SqlConnection(_connStr);

    // A failed audit write must never break the real action, so errors are swallowed here.
    public async Task AddAsync(int? userId, string? userName, string? role, string category, string details, string? ip, bool isFailed = false)
    {
        try
        {
            using var conn = Conn();
            await conn.ExecuteAsync(SP,
                new { Action = "ADD", UserId = userId, UserName = userName, Role = role, Category = category, Details = details, Ip = ip, IsFailed = isFailed },
                commandType: CommandType.StoredProcedure);
        }
        catch
        {
        }
    }

    public async Task TouchAsync(int userId, string role)
    {
        using var conn = Conn();
        await conn.ExecuteAsync(SP,
            new { Action = "TOUCH", UserId = userId, Role = role },
            commandType: CommandType.StoredProcedure);
    }

    public async Task<List<AuditLog>> ListAsync(string? category, string? search, int limit)
    {
        using var conn = Conn();
        var rows = await conn.QueryAsync<AuditLog>(SP,
            new { Action = "LIST", Category = category, Search = search, Limit = limit },
            commandType: CommandType.StoredProcedure);
        return rows.ToList();
    }

    public async Task<List<OnlineUser>> OnlineAsync()
    {
        using var conn = Conn();
        var rows = await conn.QueryAsync<OnlineUser>(SP,
            new { Action = "ONLINE" },
            commandType: CommandType.StoredProcedure);
        return rows.ToList();
    }

    public async Task<AuditStats> StatsAsync()
    {
        using var conn = Conn();
        return await conn.QuerySingleAsync<AuditStats>(SP,
            new { Action = "STATS" },
            commandType: CommandType.StoredProcedure);
    }
}

public static class AuditExtensions
{
    /// <summary>Logs an action as the signed-in user (id, name and role come from the JWT).</summary>
    public static Task LogAsync(this IAuditRepository repo, ControllerBase c, string category, string details, bool isFailed = false)
    {
        int? id = int.TryParse(c.User.FindFirstValue(ClaimTypes.NameIdentifier), out var n) ? n : null;
        return repo.AddAsync(
            id,
            c.User.FindFirstValue(ClaimTypes.Name),
            c.User.FindFirstValue(ClaimTypes.Role),
            category,
            details,
            c.HttpContext.Connection.RemoteIpAddress?.ToString(),
            isFailed);
    }
}