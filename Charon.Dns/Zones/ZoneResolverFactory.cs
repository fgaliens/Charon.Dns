using System.Globalization;
using System.Net;
using System.Net.Sockets;
using Charon.Dns.Exceptions;
using Charon.Dns.Lib.Protocol;
using Charon.Dns.Lib.Protocol.ResourceRecords;
using Charon.Dns.Lib.Server.Zones;
using Charon.Dns.Settings;
using Serilog;

namespace Charon.Dns.Zones;

public static class ZoneResolverFactory
{
    public static ZoneResolver Create(
        ZonesSettings zonesSettings,
        DnsRecordsSettings legacyDnsRecords,
        TimeSpan legacyTtl,
        ILogger logger)
    {
        var builder = new ZoneResolverBuilder();

        try
        {
            foreach (var zone in zonesSettings.Zones)
            {
                var origin = ResolveName(zone.Origin, null);
                var soa = CreateSoa(origin, zone);
                builder.AddZone(soa);

                foreach (var record in zone.Records)
                {
                    var resourceRecord = CreateRecord(record, origin);
                    var expectedRemoteAddress = ParseRemoteAddress(record);
                    builder.AddRecord(origin, resourceRecord, expectedRemoteAddress);
                }
            }

            if (legacyDnsRecords.ARecords.Count > 0)
            {
                logger.Warning("'Server:DnsRecords' is deprecated, move these records to 'Zones'");
            }

            foreach (var aRecord in legacyDnsRecords.ARecords)
            {
                var name = new Domain(aRecord.Name);
                var address = IPAddress.Parse(aRecord.Address);
                var resourceRecord = new IpAddressResourceRecord(name, address, legacyTtl);
                var expectedRemoteAddress = IPAddress.TryParse(aRecord.ResolveOnlyIfRequestCameFrom, out var remoteAddress)
                    ? remoteAddress
                    : null;
                builder.AddStandaloneRecord(resourceRecord, expectedRemoteAddress);
            }

            return builder.Build();
        }
        catch (ArgumentException e)
        {
            throw new SettingsValidationException($"Invalid zones: {e.Message}");
        }
    }

    /// <summary>
    /// "@" is the origin itself, a name with a trailing dot is absolute,
    /// any other name is relative to the origin.
    /// </summary>
    public static Domain ResolveName(string name, Domain? origin)
    {
        name = name.Trim();

        if (origin is null)
        {
            var absoluteName = name.TrimEnd('.');
            return new Domain(absoluteName);
        }

        if (name == "@")
        {
            return origin;
        }

        if (name.EndsWith('.'))
        {
            var absoluteName = name[..^1];
            return new Domain(absoluteName);
        }

        return new Domain($"{name}.{origin}");
    }

    public static IResourceRecord CreateRecord(ZonesSettings.RecordSettings record, Domain origin)
    {
        var name = ResolveName(record.Name, origin);
        var value = record.Value.Trim();
        var ttl = record.Ttl;

        switch (record.Type)
        {
            case RecordType.A:
            {
                var address = ParseIpAddress(record, AddressFamily.InterNetwork);
                return new IpAddressResourceRecord(name, address, ttl);
            }
            case RecordType.AAAA:
            {
                var address = ParseIpAddress(record, AddressFamily.InterNetworkV6);
                return new IpAddressResourceRecord(name, address, ttl);
            }
            case RecordType.CNAME:
            {
                var canonicalName = ResolveName(value, origin);
                return new CanonicalNameResourceRecord(name, canonicalName, ttl);
            }
            case RecordType.NS:
            {
                var nameServer = ResolveName(value, origin);
                return new NameServerResourceRecord(name, nameServer, ttl);
            }
            case RecordType.PTR:
            {
                var pointer = ResolveName(value, origin);
                return new PointerResourceRecord(name, pointer, ttl);
            }
            case RecordType.TXT:
            {
                var characterStrings = CharacterString.FromString(value);
                return new TextResourceRecord(name, characterStrings, ttl);
            }
            case RecordType.MX:
                return CreateMailExchange(record, name, origin);
            case RecordType.SRV:
                return CreateService(record, name, origin);
            default:
                throw new SettingsValidationException($"Record type '{record.Type}' of '{name}' is not supported in zones");
        }
    }

    private static StartOfAuthorityResourceRecord CreateSoa(Domain origin, ZonesSettings.ZoneSettings zone)
    {
        var soa = zone.Soa;
        var primaryNameServer = ResolveName(soa.PrimaryNameServer, origin);
        var responsibleMailbox = ResolveName(soa.ResponsibleMailbox, origin);

        return new StartOfAuthorityResourceRecord(
            origin,
            primaryNameServer,
            responsibleMailbox,
            soa.Serial,
            soa.Refresh,
            soa.Retry,
            soa.Expire,
            soa.MinimumTtl,
            zone.Ttl);
    }

    // "<preference> <exchange>"
    private static MailExchangeResourceRecord CreateMailExchange(
        ZonesSettings.RecordSettings record,
        Domain name,
        Domain origin)
    {
        var parts = SplitValue(record, name, 2);
        var preference = ParseNumber<int>(parts[0], record, name);
        var exchange = ResolveName(parts[1], origin);

        return new MailExchangeResourceRecord(name, preference, exchange, record.Ttl);
    }

    // "<priority> <weight> <port> <target>"
    private static ServiceResourceRecord CreateService(
        ZonesSettings.RecordSettings record,
        Domain name,
        Domain origin)
    {
        var parts = SplitValue(record, name, 4);
        var priority = ParseNumber<ushort>(parts[0], record, name);
        var weight = ParseNumber<ushort>(parts[1], record, name);
        var port = ParseNumber<ushort>(parts[2], record, name);
        var target = ResolveName(parts[3], origin);

        return new ServiceResourceRecord(name, priority, weight, port, target, record.Ttl);
    }

    private static string[] SplitValue(ZonesSettings.RecordSettings record, Domain name, int expectedPartCount)
    {
        var parts = record.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != expectedPartCount)
        {
            throw new SettingsValidationException(
                $"Value '{record.Value}' of {record.Type} record '{name}' must have {expectedPartCount} parts");
        }

        return parts;
    }

    private static T ParseNumber<T>(string rawValue, ZonesSettings.RecordSettings record, Domain name)
        where T : IParsable<T>
    {
        if (!T.TryParse(rawValue, CultureInfo.InvariantCulture, out var value))
        {
            throw new SettingsValidationException(
                $"Unable to parse '{rawValue}' in value of {record.Type} record '{name}'");
        }

        return value;
    }

    private static IPAddress ParseIpAddress(ZonesSettings.RecordSettings record, AddressFamily addressFamily)
    {
        var rawAddress = record.Value.Trim();
        if (!IPAddress.TryParse(rawAddress, out var ipAddress)
            || ipAddress.AddressFamily != addressFamily)
        {
            throw new SettingsValidationException(
                $"Value '{record.Value}' of {record.Type} record '{record.Name}' is not a valid address");
        }

        return ipAddress;
    }

    private static IPAddress? ParseRemoteAddress(ZonesSettings.RecordSettings record)
    {
        if (record.ResolveOnlyIfRequestCameFrom is null)
        {
            return null;
        }

        if (!IPAddress.TryParse(record.ResolveOnlyIfRequestCameFrom, out var remoteAddress))
        {
            throw new SettingsValidationException(
                $"ResolveOnlyIfRequestCameFrom '{record.ResolveOnlyIfRequestCameFrom}' of '{record.Name}' is not a valid address");
        }

        return remoteAddress;
    }
}
