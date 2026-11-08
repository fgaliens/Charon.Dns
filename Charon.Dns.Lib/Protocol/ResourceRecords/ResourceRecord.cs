using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Charon.Dns.Lib.Protocol.Marshalling;
using Charon.Dns.Lib.Protocol.Utils;

namespace Charon.Dns.Lib.Protocol.ResourceRecords;

public class ResourceRecord(
    Domain domain,
    byte[] data,
    RecordType type,
    RecordClass recordClass = RecordClass.IN,
    TimeSpan ttl = default)
    : IResourceRecord
{
    public static IList<ResourceRecord> GetAllFromArray(byte[] message, int offset, int count)
    {
        return GetAllFromArray(message, offset, count, out _);
    }

    public static IList<ResourceRecord> GetAllFromArray(byte[] message, int offset, int count, out int endOffset)
    {
        var records = new List<ResourceRecord>(count);

        for (var i = 0; i < count; i++)
        {
            var record = FromArray(message, offset, out offset);
            records.Add(record);
        }

        endOffset = offset;
        return records;
    }

    public static ResourceRecord FromArray(byte[] message, int offset)
    {
        return FromArray(message, offset, out _);
    }

    public static ResourceRecord FromArray(byte[] message, int offset, out int endOffset)
    {
        var domain = Domain.FromArray(message, offset, out offset);
        var tail = Struct.GetStruct<Tail>(message, offset, Tail.Size);

        offset += Tail.Size;
        var data = new byte[tail.DataLength];
        Array.Copy(message, offset, data, 0, data.Length);

        endOffset = offset + data.Length;

        return new ResourceRecord(domain, data, tail.Type, tail.Class, tail.TimeToLive);
    }

    public static ResourceRecord FromQuestion(Question question, byte[] data, TimeSpan ttl = default)
    {
        return new ResourceRecord(question.Name, data, question.Type, question.Class, ttl);
    }

    public Domain Name { get; } = domain;

    public RecordType Type { get; } = type;

    public RecordClass Class { get; } = recordClass;

    public TimeSpan TimeToLive { get; } = ttl;

    public int DataLength => Data.Length;

    public byte[] Data { get; } = data;

    public int Size => Name.Size + Tail.Size + Data.Length;

    public byte[] ToArray()
    {
        var result = new byte[Size];
        WriteTo(result);
        return result;
    }

    public void WriteTo(Span<byte> destination)
    {
        Name.WriteTo(destination);
        var offset = Name.Size;

        var tail = new Tail
        {
            Type = Type,
            Class = Class,
            TimeToLive = TimeToLive,
            DataLength = Data.Length,
        };
        Struct.GetBytes(tail, destination.Slice(offset, Tail.Size));
        offset += Tail.Size;

        Data.AsSpan().CopyTo(destination[offset..]);
    }

    public override string ToString()
    {
        return ObjectStringifier.New(this)
            .Add(nameof(Name), nameof(Type), nameof(Class), nameof(TimeToLive), nameof(DataLength))
            .ToString();
    }

    [Endian(Endianness.Big)]
    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    private struct Tail
    {
        public const int Size = 10;

        private ushort _type;
        private ushort _class;
        private uint _ttl;
        private ushort _dataLength;

        public RecordType Type
        {
            readonly get => (RecordType)_type;
            set => _type = (ushort)value;
        }

        public RecordClass Class
        {
            readonly get => (RecordClass)_class;
            set => _class = (ushort)value;
        }

        public TimeSpan TimeToLive
        {
            readonly get => TimeSpan.FromSeconds(_ttl);
            set => _ttl = (uint)value.TotalSeconds;
        }

        public int DataLength
        {
            readonly get => _dataLength;
            set => _dataLength = (ushort)value;
        }
    }
}
