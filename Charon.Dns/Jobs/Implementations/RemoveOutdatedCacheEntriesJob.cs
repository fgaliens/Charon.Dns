using Charon.Dns.Cache;
using Charon.Dns.Settings;

namespace Charon.Dns.Jobs.Implementations;

public class RemoveOutdatedCacheEntriesJob(
    IDnsCache dnsCache,
    CacheSettings cacheSettings) 
    : IJob
{
    public TimeSpan Period { get; } = cacheSettings.TimeToLive / 4;
    
    public Task Execute()
    {
        dnsCache.RemoveOutdatedResponses();
        
        return Task.CompletedTask;
    }
}
