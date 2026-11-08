using Charon.Dns.RequestResolving.ResolvingStrategies;
using Charon.Dns.Settings;
using Serilog;

namespace Charon.Dns.RequestResolving
{
    public class SafeRequestResolver(
        IResolvingStrategy resolvingStrategy,
        DnsChainSettings dnsChainSettings,
        ILogger logger) 
        : RequestResolverBase(
            resolvingStrategy,
            dnsChainSettings.RoutedServers.Select(x => x.Upstream),
            dnsChainSettings.SocketBufferSize,
            logger),
            ISafeRequestResolver;
}
