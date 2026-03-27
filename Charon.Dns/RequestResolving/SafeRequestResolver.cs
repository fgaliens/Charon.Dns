using Charon.Dns.Settings;
using Serilog;

namespace Charon.Dns.RequestResolving
{
    public class SafeRequestResolver(DnsChainSettings dnsChainSettings, ILogger globalLogger) 
        : RequestResolverBase(
            dnsChainSettings.SecuredServers.Select(x => x.Ip), 
            globalLogger), ISafeRequestResolver;
}
