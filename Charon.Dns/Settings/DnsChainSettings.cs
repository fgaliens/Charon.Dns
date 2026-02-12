using System.Net;
using Charon.Dns.Extensions;
using Microsoft.Extensions.Configuration;

namespace Charon.Dns.Settings;

public record DnsChainSettings : ISettings<DnsChainSettings>
{
    public required IReadOnlyCollection<IPAddress> DefaultServers { get; init; }
    public required IReadOnlyCollection<SecuredServerSettingsItem> SecuredServers { get; init; }

    public static DnsChainSettings Initialize(IConfiguration config)
    {
        var dnsChainConfig = config.GetSection("Server:DnsChain");
        var defaultServers = dnsChainConfig
            .GetSection("DefaultServers")
            .GetChildren()
            .Select(x => IPAddress.Parse(x.Value!))
            .ToArray();
        var securedServers = dnsChainConfig
            .GetSection("SecuredServers")
            .GetChildren()
            .Select(x =>new SecuredServerSettingsItem
            {
                Ip = x.GetSectionValue<IPAddress>("Ip"),
                InterfaceToRouteThrough = x.GetSectionValue("RouteThroughInterface"),
            })
            .ToArray();

        return new DnsChainSettings
        {
            DefaultServers = defaultServers,
            SecuredServers = securedServers,
        };
    }
}

public record SecuredServerSettingsItem
{
    public required IPAddress Ip { get; init; }
    public required string InterfaceToRouteThrough { get; init; }
}