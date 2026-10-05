using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;

public interface IReviewRepository
{
    Task<int> CreateAsync(int bookingId, int userId, int roomId, string guestName, string roomTitle,
                          int rating, string? comment);
    Task<int> UpdateAsync(int id, int userId, int rating, string? comment);
    Task<List<Review>> ListAsync(int? id, int? userId, int? roomId, int? rating);
    Task<ReviewSummary> SummaryAsync(int? roomId);
    Task<List<int>> ReviewedBookingIdsAsync(int userId);
}

public class ReviewRepository : IReviewRepository
{
    private const string SP = "SP_NORBERTODB_REVIEWS";
    private readonly string _connStr;

    public ReviewRepository(IConfiguration config)
    {
        _connStr = config.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' not found.");
    }

    private SqlConnection Conn() => new SqlConnection(_connStr);

    // Returns the new id, -1 = already reviewed, -2 = bad rating
    public async Task<int> CreateAsync(int bookingId, int userId, int roomId, string guestName, string roomTitle,
                                       int rating, string? comment)
    {
        try
        {
            using var conn = Conn();
            return await conn.ExecuteScalarAsync<int>(SP, new
            {
                Action = "CREATE",
                BookingId = bookingId,
                UserId = userId,
                RoomId = roomId,
                GuestName = guestName,
                RoomTitle = roomTitle,
                Rating = rating,
                Comment = comment
            }, commandType: CommandType.StoredProcedure);
        }
        catch (SqlException ex) when (ex.Number is 2627 or 2601)
        {
            return -1; // two taps at the same time: unique key caught the duplicate
        }
    }

    public async Task<int> UpdateAsync(int id, int userId, int rating, string? comment)
    {
        using var conn = Conn();
        return await conn.ExecuteScalarAsync<int>(SP, new
        {
            Action = "UPDATE",
            Id = id,
            UserId = userId,
            Rating = rating,
            Comment = comment
        }, commandType: CommandType.StoredProcedure);
    }

    public async Task<List<Review>> ListAsync(int? id, int? userId, int? roomId, int? rating)
    {
        using var conn = Conn();
        var rows = await conn.QueryAsync<Review>(SP, new
        {
            Action = "LIST",
            Id = id,
            UserId = userId,
            RoomId = roomId,
            Rating = rating
        }, commandType: CommandType.StoredProcedure);
        return rows.ToList();
    }

    public async Task<ReviewSummary> SummaryAsync(int? roomId)
    {
        using var conn = Conn();
        return await conn.QuerySingleAsync<ReviewSummary>(SP, new
        {
            Action = "SUMMARY",
            RoomId = roomId
        }, commandType: CommandType.StoredProcedure);
    }

    public async Task<List<int>> ReviewedBookingIdsAsync(int userId)
    {
        using var conn = Conn();
        var rows = await conn.QueryAsync<int>(SP, new
        {
            Action = "REVIEWED",
            UserId = userId
        }, commandType: CommandType.StoredProcedure);
        return rows.ToList();
    }
}