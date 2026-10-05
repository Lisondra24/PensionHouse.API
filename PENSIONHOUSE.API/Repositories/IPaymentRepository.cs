using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;

public interface IPaymentRepository
{
    Task<List<PaymentDetail>> ListAsync(string? status);
    Task<int> ReviewAsync(int paymentId, string decision, string reviewerRole, int reviewerId);
}

public class PaymentRepository : IPaymentRepository
{
    private const string SP = "SP_NORBERTODB_PAYMENTS";
    private readonly string _connStr;

    public PaymentRepository(IConfiguration config)
    {
        _connStr = config.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' not found.");
    }

    private SqlConnection Conn() => new SqlConnection(_connStr);

    public async Task<List<PaymentDetail>> ListAsync(string? status)
    {
        using var conn = Conn();
        var rows = await conn.QueryAsync<PaymentDetail>(SP,
            new { Action = "LIST", Status = status },
            commandType: CommandType.StoredProcedure);
        return rows.ToList();
    }

    public async Task<int> ReviewAsync(int paymentId, string decision, string reviewerRole, int reviewerId)
    {
        using var conn = Conn();
        return await conn.ExecuteScalarAsync<int>(SP, new
        {
            Action = "REVIEW",
            PaymentId = paymentId,
            Decision = decision,
            ReviewerRole = reviewerRole,
            ReviewerId = reviewerId
        }, commandType: CommandType.StoredProcedure);
    }
}