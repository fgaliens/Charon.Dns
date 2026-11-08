using System;

namespace Charon.Dns.Lib.Protocol.ResourceRecords;

public class NameServerResourceRecord : BaseResourceRecord
{
    public NameServerResourceRecord(IResourceRecord record, byte[] message, int dataOffset)
        : base(Rebuild(record, message, dataOffset, out var nsDomain))
    {
        NSDomainName = nsDomain;
    }

    public NameServerResourceRecord(Domain domain, Domain nsDomain, TimeSpan ttl = default)
        : base(new ResourceRecord(domain, nsDomain.ToArray(), RecordType.NS, RecordClass.IN, ttl))
    {
        NSDomainName = nsDomain;
    }

    public Domain NSDomainName { get; }

    public override string ToString()
    {
        return Stringify().Add(nameof(NSDomainName)).ToString();
    }

    private static ResourceRecord Rebuild(IResourceRecord record, byte[] message, int dataOffset, out Domain nsDomain)
    {
        nsDomain = Domain.FromArray(message, dataOffset);
        var data = nsDomain.ToArray();
        return new ResourceRecord(record.Name, data, record.Type, record.Class, record.TimeToLive);
    }
}
