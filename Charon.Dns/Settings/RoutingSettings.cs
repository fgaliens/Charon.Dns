using System.Text.RegularExpressions;
using Charon.Dns.Extensions;
using Microsoft.Extensions.Configuration;

namespace Charon.Dns.Settings;

public record RoutingSettings : ISettings<RoutingSettings>
{
    public required TimeSpan RoutingPeriod { get; init; }
    public required IReadOnlyCollection<RoutingSettingsItem> Items { get; init; }
    public required IEnumerable<string> BlockedHostNames { get; init; }

    public static RoutingSettings Initialize(IConfiguration config)
    {
        var routingSection = config.GetSection("Routing");
        
        var period = routingSection.GetSectionValue("Period", TimeSpan.Zero);
        #if DEBUG
        period = TimeSpan.FromSeconds(30);
        #endif
        
        var routingSectionItems = routingSection
            .GetSection("Items")
            .GetChildren();
        
        var blockedHostNames = routingSection
            .GetSection("BlockedHostNames")
            .GetChildren()
            .Select(x => x.GetSectionValue())
            .TryResolveDataFromFiles();

        var routingSettingsItems = new List<RoutingSettingsItem>();
        foreach (var routingSectionItem in routingSectionItems)
        {
            var interfaceToRouteThrough = routingSectionItem.GetSectionValue("InterfaceToRouteThrough");
            var ipV4RoutingSubnet = routingSectionItem.GetSectionValue<byte>("IpV4RoutingSubnet");
            var ipV6RoutingSubnet = routingSectionItem.GetSectionValue<byte>("IpV6RoutingSubnet");
            var matchedByDomainHostNames = routingSectionItem
                .GetSection("HostNameMatches:ByDomainName")
                .GetChildren()
                .Select(x => x.GetSectionValue())
                .TryResolveDataFromFiles()
                .ToArray();
            var matchedBySubstringHostNames = routingSectionItem
                .GetSection("HostNameMatches:BySubstring")
                .GetChildren()
                .Select(x => x.GetSectionValue())
                .TryResolveDataFromFiles()
                .ToArray();

            if (!Regex.IsMatch(interfaceToRouteThrough, @"^[-_\w\d]+$"))
            {
                throw new InvalidOperationException($"Invalid interface name: '{interfaceToRouteThrough}'");
            }
            
            var routingSettingsItem = new RoutingSettingsItem
            {
                InterfaceToRouteThrough = interfaceToRouteThrough,
                IpV4RoutingSubnet = ipV4RoutingSubnet,
                IpV6RoutingSubnet = ipV6RoutingSubnet,
                MatchedByDomainHostNames = matchedByDomainHostNames,
                MatchedBySubstringHostNames = matchedBySubstringHostNames,
            };
            
            routingSettingsItems.Add(routingSettingsItem);
        }

        return new RoutingSettings
        {
            RoutingPeriod = period,
            Items = routingSettingsItems,
            BlockedHostNames = blockedHostNames,
        };
    }
}

public record RoutingSettingsItem
{
    public required string InterfaceToRouteThrough { get; init; }
    public required byte IpV4RoutingSubnet { get; init; }
    public required byte IpV6RoutingSubnet { get; init; }
    public required IReadOnlyCollection<string> MatchedByDomainHostNames { get; init; }
    public required IReadOnlyCollection<string> MatchedBySubstringHostNames { get; init; }
}