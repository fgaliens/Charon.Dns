using System;
using System.Runtime.InteropServices;

namespace Charon.Dns.Lib.Protocol.ResourceRecords
{
    public class StartOfAuthorityResourceRecord : BaseResourceRecord
    {
        public StartOfAuthorityResourceRecord(IResourceRecord record, byte[] message, int dataOffset)
            : base(Rebuild(record, message, dataOffset, out var master, out var responsible, out var tail))
        {
            MasterDomainName = master;
            ResponsibleDomainName = responsible;

            SerialNumber = tail.SerialNumber;
            RefreshInterval = tail.RefreshInterval;
            RetryInterval = tail.RetryInterval;
            ExpireInterval = tail.ExpireInterval;
            MinimumTimeToLive = tail.MinimumTimeToLive;
        }

        public StartOfAuthorityResourceRecord(Domain domain, Domain master, Domain responsible, long serial,
                TimeSpan refresh, TimeSpan retry, TimeSpan expire, TimeSpan minTtl, TimeSpan ttl = default(TimeSpan)) :
            base(Create(domain, master, responsible, serial, refresh, retry, expire, minTtl, ttl))
        {
            MasterDomainName = master;
            ResponsibleDomainName = responsible;

            SerialNumber = serial;
            RefreshInterval = refresh;
            RetryInterval = retry;
            ExpireInterval = expire;
            MinimumTimeToLive = minTtl;
        }

        public StartOfAuthorityResourceRecord(Domain domain, Domain master, Domain responsible,
                Options options = default(Options), TimeSpan ttl = default(TimeSpan)) :
            this(domain, master, responsible, options.SerialNumber, options.RefreshInterval, options.RetryInterval,
                    options.ExpireInterval, options.MinimumTimeToLive, ttl)
        { }

        public Domain MasterDomainName { get; }
        public Domain ResponsibleDomainName { get; }
        public long SerialNumber { get; }
        public TimeSpan RefreshInterval { get; }
        public TimeSpan RetryInterval { get; }
        public TimeSpan ExpireInterval { get; }
        public TimeSpan MinimumTimeToLive { get; }

        public override string ToString()
        {
            return Stringify().Add("MasterDomainName", "ResponsibleDomainName", "SerialNumber").ToString();
        }

        private static IResourceRecord Create(Domain domain, Domain master, Domain responsible, long serial,
                TimeSpan refresh, TimeSpan retry, TimeSpan expire, TimeSpan minTtl, TimeSpan ttl)
        {
            var data = new byte[master.Size + responsible.Size + Options.SIZE];
            var tail = new Options()
            {
                SerialNumber = serial,
                RefreshInterval = refresh,
                RetryInterval = retry,
                ExpireInterval = expire,
                MinimumTimeToLive = minTtl
            };

            var offset = 0;
            master.WriteTo(data.AsSpan(offset, master.Size));
            offset += master.Size;
            responsible.WriteTo(data.AsSpan(offset, responsible.Size));
            offset += responsible.Size;
            Marshalling.Struct.GetBytes(tail, data.AsSpan(offset, Options.SIZE));

            return new ResourceRecord(domain, data, RecordType.SOA, RecordClass.IN, ttl);
        }

        private static IResourceRecord Rebuild(IResourceRecord record, byte[] message, int dataOffset,
                out Domain master, out Domain responsible, out Options tail)
        {
            master = Domain.FromArray(message, dataOffset, out dataOffset);
            responsible = Domain.FromArray(message, dataOffset, out dataOffset);

            tail = Marshalling.Struct.GetStruct<Options>(message, dataOffset, Options.SIZE);

            return Create(record.Name, master, responsible, tail.SerialNumber, tail.RefreshInterval,
                tail.RetryInterval, tail.ExpireInterval, tail.MinimumTimeToLive, record.TimeToLive);
        }

        [Marshalling.Endian(Marshalling.Endianness.Big)]
        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        public struct Options
        {
            public const int SIZE = 20;

            private uint serialNumber;
            private uint refreshInterval;
            private uint retryInterval;
            private uint expireInterval;
            private uint ttl;

            public long SerialNumber
            {
                get { return serialNumber; }
                set { serialNumber = (uint)value; }
            }

            public TimeSpan RefreshInterval
            {
                get { return TimeSpan.FromSeconds(refreshInterval); }
                set { refreshInterval = (uint)value.TotalSeconds; }
            }

            public TimeSpan RetryInterval
            {
                get { return TimeSpan.FromSeconds(retryInterval); }
                set { retryInterval = (uint)value.TotalSeconds; }
            }

            public TimeSpan ExpireInterval
            {
                get { return TimeSpan.FromSeconds(expireInterval); }
                set { expireInterval = (uint)value.TotalSeconds; }
            }

            public TimeSpan MinimumTimeToLive
            {
                get { return TimeSpan.FromSeconds(ttl); }
                set { ttl = (uint)value.TotalSeconds; }
            }
        }
    }
}
