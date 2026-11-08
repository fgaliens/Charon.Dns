using System;

namespace Charon.Dns.Lib.Protocol.ResourceRecords;

public class CanonicalNameResourceRecord : BaseResourceRecord
{
    public CanonicalNameResourceRecord(IResourceRecord record, byte[] message, int dataOffset)
        : base(Rebuild(record, message, dataOffset, out var cname))
    {
        CanonicalDomainName = cname;
    }

    public CanonicalNameResourceRecord(Domain domain, Domain cname, TimeSpan ttl = default)
        : base(new ResourceRecord(domain, cname.ToArray(), RecordType.CNAME, RecordClass.IN, ttl))
    {
        CanonicalDomainName = cname;
    }

    public Domain CanonicalDomainName { get; }

    public override string ToString()
    {
        return Stringify().Add(nameof(CanonicalDomainName)).ToString();
    }

    private static ResourceRecord Rebuild(IResourceRecord record, byte[] message, int dataOffset, out Domain cname)
    {
        cname = Domain.FromArray(message, dataOffset);
        var data = cname.ToArray();
        return new ResourceRecord(record.Name, data, record.Type, record.Class, record.TimeToLive);
    }
}
