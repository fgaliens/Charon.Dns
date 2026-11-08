using System;
using System.Runtime.InteropServices;
using Charon.Dns.Lib.Protocol.Marshalling;

namespace Charon.Dns.Lib.Protocol.ResourceRecords;

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
        TimeSpan refresh, TimeSpan retry, TimeSpan expire, TimeSpan minTtl, TimeSpan ttl = default)
        : base(Create(domain, master, responsible, serial, refresh, retry, expire, minTtl, ttl))
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
        Options options = default, TimeSpan ttl = default)
        : this(domain, master, responsible, options.SerialNumber, options.RefreshInterval, options.RetryInterval,
            options.ExpireInterval, options.MinimumTimeToLive, ttl)
    {
    }

    public Domain MasterDomainName { get; }
    public Domain ResponsibleDomainName { get; }
    public long SerialNumber { get; }
    public TimeSpan RefreshInterval { get; }
    public TimeSpan RetryInterval { get; }
    public TimeSpan ExpireInterval { get; }
    public TimeSpan MinimumTimeToLive { get; }

    public override string ToString()
    {
        return Stringify().Add(nameof(MasterDomainName), nameof(ResponsibleDomainName), nameof(SerialNumber)).ToString();
    }

    private static ResourceRecord Create(Domain domain, Domain master, Domain responsible, long serial,
        TimeSpan refresh, TimeSpan retry, TimeSpan expire, TimeSpan minTtl, TimeSpan ttl)
    {
        var data = new byte[master.Size + responsible.Size + Options.Size];
        var tail = new Options
        {
            SerialNumber = serial,
            RefreshInterval = refresh,
            RetryInterval = retry,
            ExpireInterval = expire,
            MinimumTimeToLive = minTtl,
        };

        var offset = 0;
        master.WriteTo(data.AsSpan(offset, master.Size));
        offset += master.Size;
        responsible.WriteTo(data.AsSpan(offset, responsible.Size));
        offset += responsible.Size;
        Struct.GetBytes(tail, data.AsSpan(offset, Options.Size));

        return new ResourceRecord(domain, data, RecordType.SOA, RecordClass.IN, ttl);
    }

    private static ResourceRecord Rebuild(IResourceRecord record, byte[] message, int dataOffset,
        out Domain master, out Domain responsible, out Options tail)
    {
        master = Domain.FromArray(message, dataOffset, out dataOffset);
        responsible = Domain.FromArray(message, dataOffset, out dataOffset);

        tail = Struct.GetStruct<Options>(message, dataOffset, Options.Size);

        return Create(record.Name, master, responsible, tail.SerialNumber, tail.RefreshInterval,
            tail.RetryInterval, tail.ExpireInterval, tail.MinimumTimeToLive, record.TimeToLive);
    }

    [Endian(Endianness.Big)]
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct Options
    {
        public const int Size = 20;

        private uint _serialNumber;
        private uint _refreshInterval;
        private uint _retryInterval;
        private uint _expireInterval;
        private uint _ttl;

        public long SerialNumber
        {
            readonly get => _serialNumber;
            set => _serialNumber = (uint)value;
        }

        public TimeSpan RefreshInterval
        {
            readonly get => TimeSpan.FromSeconds(_refreshInterval);
            set => _refreshInterval = (uint)value.TotalSeconds;
        }

        public TimeSpan RetryInterval
        {
            readonly get => TimeSpan.FromSeconds(_retryInterval);
            set => _retryInterval = (uint)value.TotalSeconds;
        }

        public TimeSpan ExpireInterval
        {
            readonly get => TimeSpan.FromSeconds(_expireInterval);
            set => _expireInterval = (uint)value.TotalSeconds;
        }

        public TimeSpan MinimumTimeToLive
        {
            readonly get => TimeSpan.FromSeconds(_ttl);
            set => _ttl = (uint)value.TotalSeconds;
        }
    }
}
