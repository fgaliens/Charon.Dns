using System.Collections.Generic;
using System.Linq;
using System.Net;
using Charon.Dns.Lib.Protocol;
using Charon.Dns.Lib.Protocol.ResourceRecords;

namespace Charon.Dns.Lib.Server.Zones;

/// <summary>
/// All records of a single domain name, grouped by record type.
/// </summary>
public class DomainRecords
{
    private readonly Dictionary<RecordType, List<ZoneRecord>> _records = new();

    public bool HasCanonicalName => _records.ContainsKey(RecordType.CNAME);

    public bool HasNonCanonicalNameRecords => _records.Keys.Any(type => type != RecordType.CNAME);

    internal void Add(ZoneRecord record)
    {
        if (!_records.TryGetValue(record.Record.Type, out var records))
        {
            records = [];
            _records.Add(record.Record.Type, records);
        }

        records.Add(record);
    }

    /// <summary>
    /// Copies records of the given type to <paramref name="destination"/>.
    /// If some of the records are bound to the remote address, only they are copied,
    /// otherwise only records without a remote address restriction are copied.
    /// </summary>
    /// <returns>Number of copied records.</returns>
    public int CopyTo(IList<IResourceRecord> destination, RecordType type, IPAddress remoteAddress)
    {
        var mappedRemoteAddress = remoteAddress.MapToIPv6();

        var foundMatchByIp = false;
        foreach (var record in Enumerate(type))
        {
            if (MatchesByIp(record, mappedRemoteAddress))
            {
                foundMatchByIp = true;
                break;
            }
        }

        var count = 0;
        foreach (var record in Enumerate(type))
        {
            var include = foundMatchByIp
                ? MatchesByIp(record, mappedRemoteAddress)
                : record.ExpectedRemoteAddress is null;

            if (include)
            {
                destination.Add(record.Record);
                count++;
            }
        }

        return count;
    }

    private static bool MatchesByIp(ZoneRecord record, IPAddress mappedRemoteAddress)
    {
        return record.ExpectedRemoteAddress?.Equals(mappedRemoteAddress) ?? false;
    }

    private IEnumerable<ZoneRecord> Enumerate(RecordType type)
    {
        if (type == RecordType.ANY)
        {
            return _records.Values.SelectMany(x => x);
        }

        return _records.TryGetValue(type, out var records) ? records : [];
    }
}
