using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;

namespace Charon.Dns.Settings;

public record RoutingSettings : ISettings<RoutingSettings>
{
    public required string InterfaceToRouteThrough { get; init; }
    public required byte IpV4RoutingSubnet { get; init; }
    public required byte IpV6RoutingSubnet { get; init; }
    public required IReadOnlyCollection<string> FullyMatchedHostNames { get; init; }
    public required IReadOnlyCollection<string> MatchedBySubstringHostNames { get; init; }

    public static RoutingSettings Initialize(IConfiguration config)
    {
        var routingSection = config.GetSection("Routing");
        var interfaceToRouteThrough = routingSection["InterfaceToRouteThrough"]!;
        var ipV4RoutingSubnet = byte.Parse(routingSection["IpV4RoutingSubnet"]!);
        var ipV6RoutingSubnet = byte.Parse(routingSection["IpV6RoutingSubnet"]!);
        var fullyMatchedHostNames = routingSection
            .GetSection("HostNames:ByFullMatch")
            .GetChildren()
            .Select(x => x.Value!)
            .ToArray();
        var matchedBySubstringHostNames = routingSection
            .GetSection("HostNames:BySubstring")
            .GetChildren()
            .Select(x => x.Value!)
            .ToArray();
        
        if (!Regex.IsMatch(interfaceToRouteThrough, @"^[-_\w\d]+$"))
        {
            throw new InvalidOperationException($"Invalid interface name: '{interfaceToRouteThrough}'");
        }

        return new RoutingSettings
        {
            InterfaceToRouteThrough = interfaceToRouteThrough,
            IpV4RoutingSubnet = ipV4RoutingSubnet,
            IpV6RoutingSubnet = ipV6RoutingSubnet,
            FullyMatchedHostNames = fullyMatchedHostNames,
            MatchedBySubstringHostNames = matchedBySubstringHostNames,
        };
    }
}