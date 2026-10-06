using Charon.Dns.RequestResolving.ResolvingStrategies;
using Charon.Dns.Settings;
using Serilog;

namespace Charon.Dns.RequestResolving
{
    public class DefaultRequestResolver(
        IResolvingStrategy resolvingStrategy,
        DnsChainSettings dnsChainSettings,
        ILogger logger) 
        : RequestResolverBase(
            resolvingStrategy,
            dnsChainSettings.DefaultServers.Select(x => x.EndPoint),
            dnsChainSettings.SocketBufferSize,
            logger),
            IDefaultRequestResolver;
}
