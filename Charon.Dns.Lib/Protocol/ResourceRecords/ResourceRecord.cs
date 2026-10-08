using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Charon.Dns.Lib.Protocol.Utils;

namespace Charon.Dns.Lib.Protocol.ResourceRecords
{
    public class ResourceRecord : IResourceRecord
    {
        private readonly Domain _domain;
        private readonly RecordType _type;
        private readonly RecordClass _recordClass;
        private readonly TimeSpan _ttl;
        private readonly byte[] _data;

        public static IList<ResourceRecord> GetAllFromArray(byte[] message, int offset, int count)
        {
            return GetAllFromArray(message, offset, count, out offset);
        }

        public static IList<ResourceRecord> GetAllFromArray(byte[] message, int offset, int count, out int endOffset)
        {
            IList<ResourceRecord> records = new List<ResourceRecord>(count);

            for (int i = 0; i < count; i++)
            {
                records.Add(FromArray(message, offset, out offset));
            }

            endOffset = offset;
            return records;
        }

        public static ResourceRecord FromArray(byte[] message, int offset)
        {
            return FromArray(message, offset, out offset);
        }

        public static ResourceRecord FromArray(byte[] message, int offset, out int endOffset)
        {
            Domain domain = Domain.FromArray(message, offset, out offset);
            Tail tail = Marshalling.Struct.GetStruct<Tail>(message, offset, Tail.SIZE);

            byte[] data = new byte[tail.DataLength];

            offset += Tail.SIZE;
            Array.Copy(message, offset, data, 0, data.Length);

            endOffset = offset + data.Length;

            return new ResourceRecord(domain, data, tail.Type, tail.Class, tail.TimeToLive);
        }

        public static ResourceRecord FromQuestion(Question question, byte[] data, TimeSpan ttl = default(TimeSpan))
        {
            return new ResourceRecord(question.Name, data, question.Type, question.Class, ttl);
        }

        public ResourceRecord(
            Domain domain, 
            byte[] data, 
            RecordType type,
            RecordClass recordClass = RecordClass.IN, 
            TimeSpan ttl = default(TimeSpan))
        {
            _domain = domain;
            _type = type;
            _recordClass = recordClass;
            _ttl = ttl;
            _data = data;
        }

        public Domain Name
        {
            get { return _domain; }
        }

        public RecordType Type
        {
            get { return _type; }
        }

        public RecordClass Class
        {
            get { return _recordClass; }
        }

        public TimeSpan TimeToLive
        {
            get { return _ttl; }
        }

        public int DataLength
        {
            get { return _data.Length; }
        }

        public byte[] Data
        {
            get { return _data; }
        }

        public int Size
        {
            get { return _domain.Size + Tail.SIZE + _data.Length; }
        }

        public byte[] ToArray()
        {
            var result = new byte[Size];
            WriteTo(result);
            return result;
        }

        public void WriteTo(Span<byte> destination)
        {
            _domain.WriteTo(destination);
            var offset = _domain.Size;

            Marshalling.Struct.GetBytes(new Tail()
            {
                Type = Type,
                Class = Class,
                TimeToLive = _ttl,
                DataLength = _data.Length
            }, destination.Slice(offset, Tail.SIZE));
            offset += Tail.SIZE;

            _data.AsSpan().CopyTo(destination[offset..]);
        }

        public override string ToString()
        {
            return ObjectStringifier.New(this)
                .Add(nameof(Name), nameof(Type), nameof(Class), nameof(TimeToLive), nameof(DataLength))
                .ToString();
        }

        [Marshalling.Endian(Marshalling.Endianness.Big)]
        [StructLayout(LayoutKind.Sequential, Pack = 2)]
        private struct Tail
        {
            public const int SIZE = 10;

            private ushort type;
            private ushort klass;
            private uint ttl;
            private ushort dataLength;

            public RecordType Type
            {
                get { return (RecordType)type; }
                set { type = (ushort)value; }
            }

            public RecordClass Class
            {
                get { return (RecordClass)klass; }
                set { klass = (ushort)value; }
            }

            public TimeSpan TimeToLive
            {
                get { return TimeSpan.FromSeconds(ttl); }
                set { ttl = (uint)value.TotalSeconds; }
            }

            public int DataLength
            {
                get { return dataLength; }
                set { dataLength = (ushort)value; }
            }
        }
    }
}
