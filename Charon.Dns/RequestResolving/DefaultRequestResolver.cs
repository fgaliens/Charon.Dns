using Charon.Dns.RequestResolving.ResolvingStrategies;
using Charon.Dns.Settings;

namespace Charon.Dns.RequestResolving
{
    public class DefaultRequestResolver(
        IResolvingStrategy resolvingStrategy,
        DnsChainSettings dnsChainSettings) 
        : RequestResolverBase(
            resolvingStrategy,
            dnsChainSettings.DefaultServers, 
            dnsChainSettings.SocketBufferSize, 
            dnsChainSettings.ResolvingConcurrencyLimit), 
            IDefaultRequestResolver;
}
