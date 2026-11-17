using System.Collections.Concurrent;
using Dapper;
using Microsoft.Data.SqlClient;

/// Answers "is this guest / staff account still enabled?" on every request,
/// so a disabled account is blocked right away instead of when its token expires.
/// The answer is remembered for 30 seconds to keep the database quiet.
public class AccountStatus
{
    private static readonly TimeSpan Remember = TimeSpan.FromSeconds(30);

    private readonly string _connStr;
    private readonly GuestRepository _guests;
    private readonly ConcurrentDictionary<string, (bool Active, DateTime At)> _cache = new();

    public AccountStatus(IConfiguration config)
    {
        _connStr = config.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' not found.");
        _guests = new GuestRepository(config);
    }

    public async Task<bool> IsActiveAsync(string role, int id)
    {
        role = (role ?? "").ToLowerInvariant();
        if (role != "user" && role != "staff") return true;   // admins have no "disabled" switch

        var key = $"{role}:{id}";
        if (_cache.TryGetValue(key, out var hit) && DateTime.UtcNow - hit.At < Remember)
            return hit.Active;

        bool active;
        try
        {
            if (role == "staff")
            {
                using var conn = new SqlConnection(_connStr);
                active = await conn.QuerySingleOrDefaultAsync<bool?>(
                    "SELECT IsActive FROM Staff WHERE Id = @Id", new { Id = id }) ?? false;
            }
            else
            {
                var guest = (await _guests.ListAsync(null, id)).FirstOrDefault();
                active = guest is null || guest.IsActive;
            }
        }
        catch
        {
            return true;   // a database hiccup must never lock everybody out
        }

        _cache[key] = (active, DateTime.UtcNow);
        return active;
    }
}