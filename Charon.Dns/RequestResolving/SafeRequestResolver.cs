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
            dnsChainSettings.SecuredServers.Select(x => x.Ip), 
            dnsChainSettings.SocketBufferSize,
            logger), 
            ISafeRequestResolver;
}
