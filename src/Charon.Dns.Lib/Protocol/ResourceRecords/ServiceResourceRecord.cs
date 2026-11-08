using System;
using System.Runtime.InteropServices;
using Charon.Dns.Lib.Protocol.Marshalling;

namespace Charon.Dns.Lib.Protocol.ResourceRecords;

public class ServiceResourceRecord : BaseResourceRecord
{
    public ServiceResourceRecord(IResourceRecord record, byte[] message, int dataOffset)
        : base(Rebuild(record, message, dataOffset, out var head, out var target))
    {
        Priority = head.Priority;
        Weight = head.Weight;
        Port = head.Port;
        Target = target;
    }

    public ServiceResourceRecord(Domain domain, ushort priority, ushort weight, ushort port, Domain target, TimeSpan ttl = default)
        : base(Create(domain, priority, weight, port, target, ttl))
    {
        Priority = priority;
        Weight = weight;
        Port = port;
        Target = target;
    }

    public ushort Priority { get; }
    public ushort Weight { get; }
    public ushort Port { get; }
    public Domain Target { get; }

    public override string ToString()
    {
        return Stringify().Add(nameof(Priority), nameof(Weight), nameof(Port), nameof(Target)).ToString();
    }

    private static ResourceRecord Create(Domain domain, ushort priority, ushort weight, ushort port, Domain target, TimeSpan ttl)
    {
        var data = new byte[Head.Size + target.Size];

        var head = new Head
        {
            Priority = priority,
            Weight = weight,
            Port = port,
        };

        Struct.GetBytes(head, data.AsSpan(0, Head.Size));
        target.WriteTo(data.AsSpan(Head.Size));

        return new ResourceRecord(domain, data, RecordType.SRV, RecordClass.IN, ttl);
    }

    private static ResourceRecord Rebuild(IResourceRecord record, byte[] message, int dataOffset, out Head head, out Domain target)
    {
        head = Struct.GetStruct<Head>(message, dataOffset, Head.Size);
        target = Domain.FromArray(message, dataOffset + Head.Size);

        return Create(record.Name, head.Priority, head.Weight, head.Port, target, record.TimeToLive);
    }

    [Endian(Endianness.Big)]
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct Head
    {
        public const int Size = 6;

        public ushort Priority { get; set; }
        public ushort Weight { get; set; }
        public ushort Port { get; set; }
    }
}
