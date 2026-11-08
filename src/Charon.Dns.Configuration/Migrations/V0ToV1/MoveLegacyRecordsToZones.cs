using System.Text.Json.Nodes;
using Charon.Dns.Configuration.Documents;
using Charon.Dns.Configuration.Json;

namespace Charon.Dns.Configuration.Migrations.V0ToV1;

/// <summary>
/// Moves "Server:DnsRecords:A" records to zones. Each name becomes a zone of its own,
/// unless it already belongs to an existing zone.
/// </summary>
internal static class MoveLegacyRecordsToZones
{
    private record LegacyRecord(string Name, string Address, string? ResolveOnlyIfRequestCameFrom);

    public static void Apply(ConfigWorkspace workspace, ICollection<string> notes)
    {
        var server = workspace.Settings.Root?.GetObject("Server");
        if (server is null || !server.TryDetachProperty("DnsRecords", out var dnsRecordsNode))
        {
            return;
        }

        var legacyRecords = ReadLegacyRecords(dnsRecordsNode);
        if (legacyRecords.Count == 0)
        {
            notes.Add("Removed empty 'Server:DnsRecords' from settings");
            return;
        }

        // Legacy records were served with the cache TTL
        var ttl = workspace.Settings.Root!.GetObject("Cache")?.GetString("TimeToLive");
        var zonesRoot = workspace.Zones.GetOrCreateRoot();
        var zones = zonesRoot.GetOrCreateArray("Zones");
        var createdZones = new HashSet<JsonObject>();

        // Parents go first, so their subdomains are added to the parent's zone
        var orderedRecords = legacyRecords
            .OrderBy(x => x.Name.Count(c => c == '.'))
            .ToArray();

        foreach (var legacyRecord in orderedRecords)
        {
            var zone = FindZone(zones, legacyRecord.Name);
            if (zone is null)
            {
                zone = CreateZone(legacyRecord.Name, ttl);
                zones.Add(zone);
                createdZones.Add(zone);
            }

            var rawOrigin = zone.GetString("Origin")!;
            var origin = NormalizeName(rawOrigin);
            var recordName = legacyRecord.Name == origin ? "@" : legacyRecord.Name + ".";
            var recordTtl = createdZones.Contains(zone) ? null : ttl;
            var record = CreateRecord(recordName, legacyRecord, recordTtl);

            var records = zone.GetOrCreateArray("Records");
            if (!records.Objects().Any(x => IsSameRecord(x, record)))
            {
                records.Add(record);
            }
        }

        notes.Add($"Moved {legacyRecords.Count} record(s) from 'Server:DnsRecords' to {workspace.Zones.FileName}");
        if (createdZones.Count > 0)
        {
            var createdZoneNames = createdZones.Select(x => x.GetString("Origin"));
            var joinedZoneNames = string.Join(", ", createdZoneNames);
            notes.Add($"Created zone(s): {joinedZoneNames}. "
                + "The server is authoritative for them now: other record types and subdomains "
                + "of these names are not forwarded to upstream servers anymore");
        }
    }

    private static List<LegacyRecord> ReadLegacyRecords(JsonNode? dnsRecordsNode)
    {
        var aRecords = (dnsRecordsNode as JsonObject)?.GetArray("A");
        var legacyRecords = new List<LegacyRecord>();

        foreach (var aRecord in aRecords.Objects())
        {
            var name = aRecord.GetString("Name");
            var address = aRecord.GetString("Address");
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(address))
            {
                var path = aRecord.GetPath();
                throw new ConfigException($"Record '{path}' in 'Server:DnsRecords' must have 'Name' and 'Address'");
            }

            var normalizedName = NormalizeName(name);
            var trimmedAddress = address.Trim();
            var resolveOnlyIfRequestCameFrom = aRecord.GetString("ResolveOnlyIfRequestCameFrom");
            legacyRecords.Add(new(normalizedName, trimmedAddress, resolveOnlyIfRequestCameFrom));
        }

        return legacyRecords;
    }

    /// <summary>
    /// Finds the closest zone the name belongs to.
    /// </summary>
    private static JsonObject? FindZone(JsonArray zones, string name)
    {
        JsonObject? foundZone = null;
        var foundOriginLength = -1;

        foreach (var zone in zones.Objects())
        {
            var rawOrigin = zone.GetString("Origin");
            if (rawOrigin is null)
            {
                continue;
            }

            var origin = NormalizeName(rawOrigin);
            var belongsToZone = name == origin || name.EndsWith("." + origin, StringComparison.Ordinal);
            if (belongsToZone && origin.Length > foundOriginLength)
            {
                foundZone = zone;
                foundOriginLength = origin.Length;
            }
        }

        return foundZone;
    }

    private static JsonObject CreateZone(string origin, string? ttl)
    {
        var zone = new JsonObject
        {
            ["Origin"] = origin,
        };

        if (ttl is not null)
        {
            zone["Ttl"] = ttl;
        }

        zone["Records"] = new JsonArray();
        return zone;
    }

    private static JsonObject CreateRecord(string name, LegacyRecord legacyRecord, string? ttl)
    {
        var record = new JsonObject
        {
            ["Name"] = name,
            ["Type"] = "A",
            ["Value"] = legacyRecord.Address,
        };

        if (ttl is not null)
        {
            record["Ttl"] = ttl;
        }

        if (legacyRecord.ResolveOnlyIfRequestCameFrom is not null)
        {
            record["ResolveOnlyIfRequestCameFrom"] = legacyRecord.ResolveOnlyIfRequestCameFrom;
        }

        return record;
    }

    private static bool IsSameRecord(JsonObject existing, JsonObject record)
    {
        return IsSameValue(existing, record, "Name", StringComparison.OrdinalIgnoreCase)
            && IsSameValue(existing, record, "Type", StringComparison.OrdinalIgnoreCase)
            && IsSameValue(existing, record, "Value", StringComparison.Ordinal)
            && IsSameValue(existing, record, "ResolveOnlyIfRequestCameFrom", StringComparison.Ordinal);
    }

    private static bool IsSameValue(JsonObject left, JsonObject right, string name, StringComparison comparison)
    {
        var leftValue = left.GetString(name)?.Trim();
        var rightValue = right.GetString(name)?.Trim();
        return string.Equals(leftValue, rightValue, comparison);
    }

    private static string NormalizeName(string name)
    {
        return name.Trim().TrimEnd('.').ToLowerInvariant();
    }
}
