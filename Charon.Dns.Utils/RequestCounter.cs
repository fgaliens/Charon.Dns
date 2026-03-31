using Charon.Dns.Lib.Server;

namespace Charon.Dns.Utils;

public class RequestCounter : IRequestCounter
{
    private ulong _requestsCount;
    
    public ulong RequestsCount => Interlocked.Read(ref _requestsCount);
    
    public ulong Increment()
    {
        return Interlocked.Increment(ref _requestsCount);
    }
}
