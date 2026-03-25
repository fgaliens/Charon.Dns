using Charon.Dns.Settings;

namespace Charon.Dns.RequestResolving
{
    public class DefaultRequestResolver(DnsChainSettings dnsChainSettings) 
        : RequestResolverBase(dnsChainSettings.DefaultServers, dnsChainSettings.ResolvingConcurrencyLimit), IDefaultRequestResolver;
}
