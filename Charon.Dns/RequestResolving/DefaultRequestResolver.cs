using Charon.Dns.Settings;
using Serilog;

namespace Charon.Dns.RequestResolving
{
    public class DefaultRequestResolver(DnsChainSettings dnsChainSettings, ILogger globalLogger) 
        : RequestResolverBase(dnsChainSettings.DefaultServers, globalLogger), IDefaultRequestResolver;
}
