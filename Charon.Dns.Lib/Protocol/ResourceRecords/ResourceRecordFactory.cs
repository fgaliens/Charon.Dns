using System.Collections.Generic;

namespace Charon.Dns.Lib.Protocol.ResourceRecords;

public static class ResourceRecordFactory
{
    public static IList<IResourceRecord> GetAllFromArray(byte[] message, int offset, int count)
    {
        return GetAllFromArray(message, offset, count, out _);
    }

    public static IList<IResourceRecord> GetAllFromArray(byte[] message, int offset, int count, out int endOffset)
    {
        var result = new List<IResourceRecord>(count);

        for (var i = 0; i < count; i++)
        {
            var record = FromArray(message, offset, out offset);
            result.Add(record);
        }

        endOffset = offset;
        return result;
    }

    public static IResourceRecord FromArray(byte[] message, int offset)
    {
        return FromArray(message, offset, out _);
    }

    public static IResourceRecord FromArray(byte[] message, int offset, out int endOffset)
    {
        var record = ResourceRecord.FromArray(message, offset, out endOffset);
        var dataOffset = endOffset - record.DataLength;

        return record.Type switch
        {
            RecordType.A or RecordType.AAAA => new IpAddressResourceRecord(record),
            RecordType.NS => new NameServerResourceRecord(record, message, dataOffset),
            RecordType.CNAME => new CanonicalNameResourceRecord(record, message, dataOffset),
            RecordType.SOA => new StartOfAuthorityResourceRecord(record, message, dataOffset),
            RecordType.PTR => new PointerResourceRecord(record, message, dataOffset),
            RecordType.MX => new MailExchangeResourceRecord(record, message, dataOffset),
            RecordType.TXT => new TextResourceRecord(record),
            RecordType.SRV => new ServiceResourceRecord(record, message, dataOffset),
            _ => record,
        };
    }
}
