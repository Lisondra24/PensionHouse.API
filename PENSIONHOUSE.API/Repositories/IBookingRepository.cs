using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;

public interface IBookingRepository
{
    Task<int> CreateAsync(int? userId, string? guestName, string? guestContact, int roomId, int rateId,
                          DateTime checkIn, string source, string paymentMethod, string? referenceCode,
                          string? idType = null);
    Task<List<BookingDetail>> ListAsync(int? userId, string? status, int? bookingId, string? source);
    Task<int> UpdateStatusAsync(int bookingId, string status, int? userId);
}

public class BookingRepository : IBookingRepository
{
    private const string SP = "SP_NORBERTODB_BOOKINGS";
    private readonly string _connStr;

    public BookingRepository(IConfiguration config)
    {
        _connStr = config.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' not found.");
    }

    private SqlConnection Conn() => new SqlConnection(_connStr);

    public async Task<int> CreateAsync(int? userId, string? guestName, string? guestContact, int roomId, int rateId,
                                       DateTime checkIn, string source, string paymentMethod, string? referenceCode,
                                       string? idType = null)
    {
        using var conn = Conn();
        return await conn.ExecuteScalarAsync<int>(SP, new
        {
            Action = "CREATE",
            UserId = userId,
            GuestName = guestName,
            GuestContact = guestContact,
            RoomId = roomId,
            RateId = rateId,
            CheckIn = checkIn,
            Source = source,
            PaymentMethod = paymentMethod,
            ReferenceCode = referenceCode,
            IdType = idType
        }, commandType: CommandType.StoredProcedure);
    }

    public async Task<List<BookingDetail>> ListAsync(int? userId, string? status, int? bookingId, string? source)
    {
        using var conn = Conn();
        var rows = await conn.QueryAsync<BookingDetail>(SP, new
        {
            Action = "LIST",
            UserId = userId,
            Status = status,
            BookingId = bookingId,
            Source = source
        }, commandType: CommandType.StoredProcedure);
        return rows.ToList();
    }

    public async Task<int> UpdateStatusAsync(int bookingId, string status, int? userId)
    {
        using var conn = Conn();
        return await conn.ExecuteScalarAsync<int>(SP, new
        {
            Action = "UPDATE_STATUS",
            BookingId = bookingId,
            Status = status,
            UserId = userId
        }, commandType: CommandType.StoredProcedure);
    }
}