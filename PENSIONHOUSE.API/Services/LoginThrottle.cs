using System.Collections.Concurrent;

/// Counts wrong sign-ins and blocks more tries for a while.
///  - 5 wrong passwords for the same email from the same device -> blocked 15 minutes
///  - 20 wrong passwords from one device (any email)            -> blocked 15 minutes
/// A correct sign-in clears the email counter. Counters live in memory,
/// so they reset when the API restarts (fine for this system).
public class LoginThrottle
{
    private const int MaxPerEmailAndIp = 5;
    private const int MaxPerIp = 20;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    private class Entry
    {
        public int Count;
        public DateTime First;
        public DateTime? LockedUntil;
    }

    private readonly ConcurrentDictionary<string, Entry> _items = new();

    private static string EmailKey(string email, string? ip) => $"e|{email}|{ip}";
    private static string IpKey(string? ip) => $"i|{ip}";

    /// Seconds the caller still has to wait, or 0 when sign-in is allowed
    public int SecondsLocked(string email, string? ip)
        => Math.Max(Remaining(EmailKey(email, ip)), Remaining(IpKey(ip)));

    public void Fail(string email, string? ip)
    {
        Bump(EmailKey(email, ip), MaxPerEmailAndIp);
        Bump(IpKey(ip), MaxPerIp);
    }

    public void Success(string email, string? ip)
        => _items.TryRemove(EmailKey(email, ip), out _);

    public static string Describe(int seconds)
        => seconds >= 60 ? $"{(int)Math.Ceiling(seconds / 60.0)} minute(s)" : $"{seconds} second(s)";

    private int Remaining(string key)
    {
        if (!_items.TryGetValue(key, out var e)) return 0;
        lock (e)
        {
            if (e.LockedUntil is { } until && until > DateTime.UtcNow)
                return (int)Math.Ceiling((until - DateTime.UtcNow).TotalSeconds);
            return 0;
        }
    }

    private void Bump(string key, int max)
    {
        var now = DateTime.UtcNow;
        var e = _items.GetOrAdd(key, _ => new Entry { First = now });
        lock (e)
        {
            var unlocked = e.LockedUntil is null || e.LockedUntil <= now;
            if (unlocked && now - e.First > Window)
            {
                e.Count = 0;
                e.First = now;
                e.LockedUntil = null;
            }
            e.Count++;
            if (e.Count >= max) e.LockedUntil = now + Window;
        }
        if (_items.Count > 5000) Cleanup(now);
    }

    private void Cleanup(DateTime now)
    {
        foreach (var kv in _items)
            if (now - kv.Value.First > Window &&
                (kv.Value.LockedUntil is null || kv.Value.LockedUntil <= now))
                _items.TryRemove(kv.Key, out _);
    }
}