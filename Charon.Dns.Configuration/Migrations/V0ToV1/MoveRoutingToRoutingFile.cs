using System.Text.Json.Nodes;
using Charon.Dns.Configuration.Documents;
using Charon.Dns.Configuration.Json;

namespace Charon.Dns.Configuration.Migrations.V0ToV1;

/// <summary>
/// Moves "Routing" from settings to the routing file, renaming its fields on the way.
/// The routing file did not exist before 2.0, so an existing one is already in the new format.
/// </summary>
internal static class MoveRoutingToRoutingFile
{
    public static void Apply(ConfigWorkspace workspace, ICollection<string> notes)
    {
        var settings = workspace.Settings.Root;
        if (settings?.GetProperty("Routing") is not { } routingNode)
        {
            return;
        }

        if (routingNode is not JsonObject routing)
        {
            throw new ConfigException($"'Routing' in {workspace.Settings.FileName} must be a JSON object");
        }

        if (workspace.Routing.Root?.HasProperty("Routing") == true)
        {
            throw new ConfigException(
                $"'Routing' is set both in {workspace.Settings.FileName} and {workspace.Routing.FileName}, "
                + $"remove it from one of the files and run the migration again");
        }

        settings.RemoveProperty("Routing");
        RenameFields(routing);

        var routingRoot = workspace.Routing.GetOrCreateRoot();
        routingRoot.Add("Routing", routing);

        notes.Add($"Moved 'Routing' from {workspace.Settings.FileName} to {workspace.Routing.FileName}");
    }

    private static void RenameFields(JsonObject routing)
    {
        routing.RenameProperty("Period", "RouteIdleTimeout");
        routing.RenameProperty("BlockedHostNames", "BlockedHosts");
        routing.RenameProperty("Items", "Rules");

        var rules = routing.GetArray("Rules");
        foreach (var rule in rules.Objects())
        {
            rule.RenameProperty("InterfaceToRouteThrough", "Interface");
            rule.RenameProperty("IpV4RoutingSubnet", "IPv4PrefixLength");
            rule.RenameProperty("IpV6RoutingSubnet", "IPv6PrefixLength");
            rule.RenameProperty("HostNameMatches", "Match");

            var match = rule.GetObject("Match");
            match?.RenameProperty("ByDomainName", "Domains");
            match?.RenameProperty("BySubstring", "Substrings");
        }
    }
}
