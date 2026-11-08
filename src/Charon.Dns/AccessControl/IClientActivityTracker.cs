using System.Net;

namespace Charon.Dns.AccessControl;

public interface IClientActivityTracker
{
    void RecordActivity(IPAddress ip);
    DateTimeOffset? GetLastSeen(IPAddress ip);
    IReadOnlyCollection<IPAddress> GetTrackedIps();
}
