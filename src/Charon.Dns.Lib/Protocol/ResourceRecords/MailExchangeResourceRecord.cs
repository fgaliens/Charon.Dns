using System;
using System.Buffers.Binary;

namespace Charon.Dns.Lib.Protocol.ResourceRecords;

public class MailExchangeResourceRecord : BaseResourceRecord
{
    private const int PreferenceSize = 2;

    public MailExchangeResourceRecord(IResourceRecord record, byte[] message, int dataOffset)
        : base(Rebuild(record, message, dataOffset, out var preference, out var exchange))
    {
        Preference = preference;
        ExchangeDomainName = exchange;
    }

    public MailExchangeResourceRecord(Domain domain, int preference, Domain exchange, TimeSpan ttl = default)
        : base(Create(domain, preference, exchange, ttl))
    {
        Preference = preference;
        ExchangeDomainName = exchange;
    }

    public int Preference { get; }
    public Domain ExchangeDomainName { get; }

    public override string ToString()
    {
        return Stringify().Add(nameof(Preference), nameof(ExchangeDomainName)).ToString();
    }

    private static ResourceRecord Create(Domain domain, int preference, Domain exchange, TimeSpan ttl)
    {
        var data = new byte[PreferenceSize + exchange.Size];

        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(0, PreferenceSize), (ushort)preference);
        exchange.WriteTo(data.AsSpan(PreferenceSize));

        return new ResourceRecord(domain, data, RecordType.MX, RecordClass.IN, ttl);
    }

    private static ResourceRecord Rebuild(IResourceRecord record, byte[] message, int dataOffset, out int preference, out Domain exchange)
    {
        preference = BinaryPrimitives.ReadUInt16BigEndian(message.AsSpan(dataOffset, PreferenceSize));
        exchange = Domain.FromArray(message, dataOffset + PreferenceSize);

        return Create(record.Name, preference, exchange, record.TimeToLive);
    }
}
