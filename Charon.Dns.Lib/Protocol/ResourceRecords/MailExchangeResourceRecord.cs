using System;
using System.Buffers.Binary;

namespace Charon.Dns.Lib.Protocol.ResourceRecords
{
    public class MailExchangeResourceRecord : BaseResourceRecord
    {
        private const int PREFERENCE_SIZE = 2;

        public MailExchangeResourceRecord(IResourceRecord record, byte[] message, int dataOffset)
            : base(Rebuild(record, message, dataOffset, out var preference, out var exchange))
        {
            Preference = preference;
            ExchangeDomainName = exchange;
        }

        public MailExchangeResourceRecord(Domain domain, int preference, Domain exchange, TimeSpan ttl = default(TimeSpan)) :
            base(Create(domain, preference, exchange, ttl))
        {
            Preference = preference;
            ExchangeDomainName = exchange;
        }

        public int Preference { get; }
        public Domain ExchangeDomainName { get; }

        public override string ToString()
        {
            return Stringify().Add("Preference", "ExchangeDomainName").ToString();
        }

        private static IResourceRecord Create(Domain domain, int preference, Domain exchange, TimeSpan ttl)
        {
            var data = new byte[PREFERENCE_SIZE + exchange.Size];

            BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(0, PREFERENCE_SIZE), (ushort)preference);
            exchange.WriteTo(data.AsSpan(PREFERENCE_SIZE));

            return new ResourceRecord(domain, data, RecordType.MX, RecordClass.IN, ttl);
        }

        private static IResourceRecord Rebuild(IResourceRecord record, byte[] message, int dataOffset, out int preference, out Domain exchange)
        {
            preference = BinaryPrimitives.ReadUInt16BigEndian(message.AsSpan(dataOffset, PREFERENCE_SIZE));
            dataOffset += PREFERENCE_SIZE;

            exchange = Domain.FromArray(message, dataOffset);

            return Create(record.Name, preference, exchange, record.TimeToLive);
        }
    }
}
