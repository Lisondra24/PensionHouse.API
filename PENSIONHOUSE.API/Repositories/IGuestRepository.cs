using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;

public interface IGuestRepository
{
    Task<List<Guest>> ListAsync(string? search, int? userId);
    Task<int> SetActiveAsync(int userId, bool isActive);
}

public class GuestRepository : IGuestRepository
{
    private const string SP = "SP_NORBERTODB_GUESTS";
    private readonly string _connStr;

    public GuestRepository(IConfiguration config)
    {
        _connStr = config.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' not found.");
    }

    private SqlConnection Conn() => new SqlConnection(_connStr);

    public async Task<List<Guest>> ListAsync(string? search, int? userId)
    {
        using var conn = Conn();
        var rows = await conn.QueryAsync<Guest>(SP,
            new { Action = "LIST", Search = search, UserId = userId },
            commandType: CommandType.StoredProcedure);
        return rows.ToList();
    }

    public async Task<int> SetActiveAsync(int userId, bool isActive)
    {
        using var conn = Conn();
        return await conn.ExecuteScalarAsync<int>(SP,
            new { Action = "SET_ACTIVE", UserId = userId, IsActive = isActive },
            commandType: CommandType.StoredProcedure);
    }
}