using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;

public class AuthRepository : IAuthRepository
{
    private readonly string _connStr;

    public AuthRepository(IConfiguration config)
    {
        _connStr = config.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' not found.");
    }

    private SqlConnection Conn() => new SqlConnection(_connStr);

    public async Task<IEnumerable<Account>> GetLoginAccountsAsync(string email, string loginType)
    {
        using var conn = Conn();
        return await conn.QueryAsync<Account>(
            "SP_NORBERTODB_LOGIN",
            new { Email = email, LoginType = loginType },
            commandType: CommandType.StoredProcedure);
    }

    public async Task<int> CreateAccountAsync(string role, string fullName, string email, string passwordHash,
                                          string? contactNo, string? idType = null, string? idImageFile = null)
    {
        using var conn = Conn();
        return await conn.ExecuteScalarAsync<int>(
            "SP_NORBERTODB_CREATE_ACCOUNT",
            new
            {
                Role = role,
                FullName = fullName,
                Email = email,
                PasswordHash = passwordHash,
                ContactNo = contactNo,
                IdType = idType,
                IdImageFile = idImageFile
            },
            commandType: CommandType.StoredProcedure);
    }
}