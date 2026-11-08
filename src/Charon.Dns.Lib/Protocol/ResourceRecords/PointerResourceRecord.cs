using System;
using System.Net;

namespace Charon.Dns.Lib.Protocol.ResourceRecords;

public class PointerResourceRecord : BaseResourceRecord
{
    public PointerResourceRecord(IResourceRecord record, byte[] message, int dataOffset)
        : base(Rebuild(record, message, dataOffset, out var pointer))
    {
        PointerDomainName = pointer;
    }

    public PointerResourceRecord(IPAddress ip, Domain pointer, TimeSpan ttl = default)
        : this(Domain.PointerName(ip), pointer, ttl)
    {
    }

    public PointerResourceRecord(Domain name, Domain pointer, TimeSpan ttl = default)
        : base(new ResourceRecord(name, pointer.ToArray(), RecordType.PTR, RecordClass.IN, ttl))
    {
        PointerDomainName = pointer;
    }

    public Domain PointerDomainName { get; }

    public override string ToString()
    {
        return Stringify().Add(nameof(PointerDomainName)).ToString();
    }

    private static ResourceRecord Rebuild(IResourceRecord record, byte[] message, int dataOffset, out Domain pointer)
    {
        pointer = Domain.FromArray(message, dataOffset);
        var data = pointer.ToArray();
        return new ResourceRecord(record.Name, data, record.Type, record.Class, record.TimeToLive);
    }
}
