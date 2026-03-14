using Charon.Dns.Extensions;
using Microsoft.Extensions.Configuration;

namespace Charon.Dns.Settings;

public class CacheSettings : ISettings<CacheSettings>
{
    public required TimeSpan TimeToLive { get; init; }
    
    public static CacheSettings Initialize(IConfiguration config)
    {
        var cacheSection = config.GetSection("Cache");
        var timeToLive = cacheSection.GetSectionValue("TimeToLive", TimeSpan.Zero);
        
#if DEBUG
        timeToLive = TimeSpan.FromSeconds(30);
#endif

        return new()
        {
            TimeToLive = timeToLive,
        };
    }
}
