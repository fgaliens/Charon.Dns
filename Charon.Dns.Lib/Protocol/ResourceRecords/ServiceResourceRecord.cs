using System;
using System.Runtime.InteropServices;

namespace Charon.Dns.Lib.Protocol.ResourceRecords
{
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

        public ServiceResourceRecord(Domain domain, ushort priority, ushort weight, ushort port, Domain target, TimeSpan ttl = default(TimeSpan)) :
                base(Create(domain, priority, weight, port, target, ttl))
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
            return Stringify().Add("Priority", "Weight", "Port", "Target").ToString();
        }

        private static IResourceRecord Create(Domain domain, ushort priority, ushort weight, ushort port, Domain target, TimeSpan ttl)
        {
            var data = new byte[Head.SIZE + target.Size];

            var head = new Head()
            {
                Priority = priority,
                Weight = weight,
                Port = port
            };

            Marshalling.Struct.GetBytes(head, data.AsSpan(0, Head.SIZE));
            target.WriteTo(data.AsSpan(Head.SIZE));

            return new ResourceRecord(domain, data, RecordType.SRV, RecordClass.IN, ttl);
        }

        private static IResourceRecord Rebuild(IResourceRecord record, byte[] message, int dataOffset, out Head head, out Domain target)
        {
            head = Marshalling.Struct.GetStruct<Head>(message, dataOffset, Head.SIZE);
            target = Domain.FromArray(message, dataOffset + Head.SIZE);

            return Create(record.Name, head.Priority, head.Weight, head.Port, target, record.TimeToLive);
        }

        [Marshalling.Endian(Marshalling.Endianness.Big)]
        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        private struct Head
        {
            public const int SIZE = 6;

            private ushort priority;
            private ushort weight;
            private ushort port;

            public ushort Priority
            {
                get { return priority; }
                set { priority = value; }
            }

            public ushort Weight
            {
                get { return weight; }
                set { weight = value; }
            }

            public ushort Port
            {
                get { return port; }
                set { port = value; }
            }
        }
    }
}
