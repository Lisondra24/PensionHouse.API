using System.Data;
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;

public interface IRoomRepository
{
    Task<List<Room>> ListAsync(bool includeInactive);
    Task<List<Room>> AvailableAsync(DateTime checkIn, int hours);
    Task<int> CreateAsync(RoomRequest r);
    Task<int> UpdateAsync(int id, RoomRequest r);
    Task<int> SetActiveAsync(int id, bool isActive);
    Task<int> SetMaintenanceAsync(int id, bool isMaintenance);
    Task<int> AddRateAsync(int roomId, RateRequest r);
    Task<int> UpdateRateAsync(int rateId, RateRequest r);
    Task<int> DeleteRateAsync(int rateId);
}

public class RoomRepository : IRoomRepository
{
    private const string SP = "SP_NORBERTODB_ROOMS";
    private readonly string _connStr;

    public RoomRepository(IConfiguration config)
    {
        _connStr = config.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' not found.");
    }

    private SqlConnection Conn() => new SqlConnection(_connStr);

    private async Task<int> ExecAsync(object param)
    {
        using var conn = Conn();
        return await conn.ExecuteScalarAsync<int>(SP, param, commandType: CommandType.StoredProcedure);
    }

    private async Task<List<Room>> QueryRoomsAsync(object param)
    {
        using var conn = Conn();
        using var multi = await conn.QueryMultipleAsync(SP, param, commandType: CommandType.StoredProcedure);

        var rooms = (await multi.ReadAsync<Room>()).ToList();
        var rates = (await multi.ReadAsync<RoomRate>()).ToList();
        var photos = (await multi.ReadAsync<RoomPhotoRow>()).ToList();

        foreach (var room in rooms)
        {
            room.Rates = rates.Where(x => x.RoomId == room.Id).ToList();
            room.Photos = photos.Where(x => x.RoomId == room.Id).Select(x => x.Url).ToList();
        }

        return rooms;
    }

    // null = leave the room's photos unchanged; a list (even empty) replaces them.
    private static string? PhotosJson(RoomRequest r)
        => r.Photos is null ? null : JsonSerializer.Serialize(r.Photos);

    public Task<List<Room>> ListAsync(bool includeInactive)
        => QueryRoomsAsync(new { Action = "LIST", IncludeInactive = includeInactive });

    public Task<List<Room>> AvailableAsync(DateTime checkIn, int hours)
        => QueryRoomsAsync(new { Action = "AVAILABLE", CheckIn = checkIn, Hours = hours });

    public Task<int> CreateAsync(RoomRequest r)
        => ExecAsync(new { Action = "CREATE", r.RoomNo, r.Title, r.Description, r.Capacity, r.ImageUrl, Photos = PhotosJson(r) });

    public Task<int> UpdateAsync(int id, RoomRequest r)
        => ExecAsync(new { Action = "UPDATE", RoomId = id, r.RoomNo, r.Title, r.Description, r.Capacity, r.ImageUrl, Photos = PhotosJson(r) });

    public Task<int> SetActiveAsync(int id, bool isActive)
        => ExecAsync(new { Action = "SET_ACTIVE", RoomId = id, IsActive = isActive });

    public Task<int> SetMaintenanceAsync(int id, bool isMaintenance)
        => ExecAsync(new { Action = "SET_MAINTENANCE", RoomId = id, IsMaintenance = isMaintenance });

    public Task<int> AddRateAsync(int roomId, RateRequest r)
        => ExecAsync(new { Action = "ADD_RATE", RoomId = roomId, r.Label, r.Hours, r.Price });

    public Task<int> UpdateRateAsync(int rateId, RateRequest r)
        => ExecAsync(new { Action = "UPDATE_RATE", RateId = rateId, r.Label, r.Hours, r.Price });

    public Task<int> DeleteRateAsync(int rateId)
        => ExecAsync(new { Action = "DELETE_RATE", RateId = rateId });
}