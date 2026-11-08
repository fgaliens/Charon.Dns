using System;
using System.Net;

namespace Charon.Dns.Lib.Protocol.ResourceRecords;

public class IpAddressResourceRecord : BaseResourceRecord
{
    public IpAddressResourceRecord(IResourceRecord record) : base(record)
    {
        IpAddress = new IPAddress(Data);
    }

    public IpAddressResourceRecord(Domain domain, IPAddress ip, TimeSpan ttl = default)
        : base(Create(domain, ip, ttl))
    {
        IpAddress = ip;
    }

    public IPAddress IpAddress { get; }

    public override string ToString()
    {
        return Stringify().Add(nameof(IpAddress)).ToString();
    }

    private static ResourceRecord Create(Domain domain, IPAddress ip, TimeSpan ttl)
    {
        var data = ip.GetAddressBytes();
        var type = data.Length == 4 ? RecordType.A : RecordType.AAAA;

        return new ResourceRecord(domain, data, type, RecordClass.IN, ttl);
    }
}
