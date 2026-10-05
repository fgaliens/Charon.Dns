using System.Collections.Concurrent;
using System.Net;
using Charon.Dns.Utils;

namespace Charon.Dns.AccessControl;

public class ClientActivityTracker(IDateTimeProvider dateTimeProvider) : IClientActivityTracker
{
    private readonly ConcurrentDictionary<IPAddress, DateTimeOffset> _lastSeen = new();

    public void RecordActivity(IPAddress ip)
    {
        _lastSeen[ip] = dateTimeProvider.UtcNow;
    }

    public DateTimeOffset? GetLastSeen(IPAddress ip)
    {
        return _lastSeen.TryGetValue(ip, out var lastSeen) ? lastSeen : null;
    }

    public IReadOnlyCollection<IPAddress> GetTrackedIps()
    {
        return _lastSeen.Keys.ToArray();
    }
}
