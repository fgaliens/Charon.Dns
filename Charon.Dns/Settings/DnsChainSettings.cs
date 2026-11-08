using Charon.Dns.Extensions;
using Charon.Dns.RequestResolving.ResolvingStrategies;
using Charon.Dns.Utils.Units;
using Microsoft.Extensions.Configuration;

namespace Charon.Dns.Settings;

public record DnsChainSettings : ISettings<DnsChainSettings>
{
    public required ResolvingStrategy ResolvingStrategy { get; init; }
    public required ByteUnit SocketBufferSize { get; init; }
    public required IReadOnlyCollection<DnsServerAddress> DefaultServers { get; init; }
    public required IReadOnlyCollection<RoutedServerSettingsItem> RoutedServers { get; init; }

    public static DnsChainSettings Initialize(IConfiguration config)
    {
        var upstreamConfig = config.GetSection("Upstream");
        var resolvingStrategy = upstreamConfig.GetSectionEnumValue("ResolvingStrategy", ResolvingStrategy.RoundRobin);
        var socketBufferSize = upstreamConfig.GetSectionValue("SocketBufferSize", new ByteUnit(512 * 1024));
        var defaultServers = upstreamConfig
            .GetSection("DefaultServers")
            .GetChildren()
            .Select(x => x.GetSectionValue<DnsServerAddress>())
            .ToArray();
        var routedServers = upstreamConfig
            .GetSection("RoutedServers")
            .GetChildren()
            .Select(x => new RoutedServerSettingsItem
            {
                Address = x.GetSectionValue<DnsServerAddress>("Address"),
                InterfaceToRouteThrough = x.GetSectionValue("Interface"),
            })
            .ToArray();

        return new DnsChainSettings
        {
            ResolvingStrategy = resolvingStrategy,
            SocketBufferSize = socketBufferSize,
            DefaultServers = defaultServers,
            RoutedServers = routedServers,
        };
    }
}

public record RoutedServerSettingsItem
{
    public required DnsServerAddress Address { get; init; }
    public required string InterfaceToRouteThrough { get; init; }
}