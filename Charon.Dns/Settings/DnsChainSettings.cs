using Charon.Dns.Extensions;
using Charon.Dns.RequestResolving.ResolvingStrategies;
using Charon.Dns.Utils.Units;
using Microsoft.Extensions.Configuration;

namespace Charon.Dns.Settings;

public record DnsChainSettings : ISettings<DnsChainSettings>
{
    public required ResolvingStrategy ResolvingStrategy { get; init; }
    public required int ResolvingConcurrencyLimit { get; init; }
    public required ByteUnit SocketBufferSize { get; init; }
    public required IReadOnlyCollection<DnsServerAddress> DefaultServers { get; init; }
    public required IReadOnlyCollection<SecuredServerSettingsItem> SecuredServers { get; init; }

    public static DnsChainSettings Initialize(IConfiguration config)
    {
        var dnsChainConfig = config.GetSection("Server:DnsChain");
        var resolvingStrategy = dnsChainConfig.GetSectionEnumValue("ResolvingStrategy", ResolvingStrategy.RoundRobin);
        var resolvingConcurrencyLimit = dnsChainConfig.GetSectionValue("ResolvingConcurrencyLimit", 2);
        resolvingConcurrencyLimit = Math.Max(0, resolvingConcurrencyLimit);
        var socketBufferSize = dnsChainConfig.GetSectionValue("SocketBufferSize", new ByteUnit(512 * 1024));
        var defaultServers = dnsChainConfig
            .GetSection("DefaultServers")
            .GetChildren()
            .Select(x => x.GetSectionValue<DnsServerAddress>())
            .ToArray();
        var securedServers = dnsChainConfig
            .GetSection("SecuredServers")
            .GetChildren()
            .Select(x =>new SecuredServerSettingsItem
            {
                Address = x.GetSectionValue<DnsServerAddress>("Ip"),
                InterfaceToRouteThrough = x.GetSectionValue("RouteThroughInterface"),
            })
            .ToArray();

        return new DnsChainSettings
        {
            ResolvingStrategy = resolvingStrategy,
            ResolvingConcurrencyLimit = resolvingConcurrencyLimit,
            SocketBufferSize = socketBufferSize,
            DefaultServers = defaultServers,
            SecuredServers = securedServers,
        };
    }
}

public record SecuredServerSettingsItem
{
    public required DnsServerAddress Address { get; init; }
    public required string InterfaceToRouteThrough { get; init; }
}