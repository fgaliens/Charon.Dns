using System;

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
            byte[] pref = BitConverter.GetBytes((ushort)preference);
            byte[] data = new byte[pref.Length + exchange.Size];

            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(pref);
            }

            pref.CopyTo(data, 0);
            exchange.ToArray().CopyTo(data, pref.Length);

            return new ResourceRecord(domain, data, RecordType.MX, RecordClass.IN, ttl);
        }

        private static IResourceRecord Rebuild(IResourceRecord record, byte[] message, int dataOffset, out int preference, out Domain exchange)
        {
            byte[] preferenceBytes = new byte[PREFERENCE_SIZE];
            Array.Copy(message, dataOffset, preferenceBytes, 0, preferenceBytes.Length);

            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(preferenceBytes);
            }

            dataOffset += PREFERENCE_SIZE;

            preference = BitConverter.ToUInt16(preferenceBytes, 0);
            exchange = Domain.FromArray(message, dataOffset);

            return Create(record.Name, preference, exchange, record.TimeToLive);
        }
    }
}
