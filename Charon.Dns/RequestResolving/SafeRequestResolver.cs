using Charon.Dns.RequestResolving.ResolvingStrategies;
using Charon.Dns.Settings;

namespace Charon.Dns.RequestResolving
{
    public class SafeRequestResolver(
        IResolvingStrategy resolvingStrategy,
        DnsChainSettings dnsChainSettings) 
        : RequestResolverBase(
            resolvingStrategy,
            dnsChainSettings.SecuredServers.Select(x => x.Ip), 
            dnsChainSettings.SocketBufferSize,
            dnsChainSettings.ResolvingConcurrencyLimit), 
            ISafeRequestResolver;
}
