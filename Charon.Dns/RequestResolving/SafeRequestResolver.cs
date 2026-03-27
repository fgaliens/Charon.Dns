using Charon.Dns.Settings;

namespace Charon.Dns.RequestResolving
{
    public class SafeRequestResolver(DnsChainSettings dnsChainSettings) 
        : RequestResolverBase(
            dnsChainSettings.SecuredServers.Select(x => x.Ip), 
            dnsChainSettings.ResolvingConcurrencyLimit), ISafeRequestResolver;
}
