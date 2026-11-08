using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Charon.Dns.Lib.Protocol.Marshalling;
using Charon.Dns.Lib.Protocol.Utils;

namespace Charon.Dns.Lib.Protocol;

public class Question(Domain domain, RecordType type = RecordType.A, RecordClass klass = RecordClass.IN)
    : IMessageEntry
{
    public static IList<Question> GetAllFromArray(byte[] message, int offset, int questionCount)
    {
        return GetAllFromArray(message, offset, questionCount, out _);
    }

    public static IList<Question> GetAllFromArray(byte[] message, int offset, int questionCount, out int endOffset)
    {
        var questions = new List<Question>(questionCount);

        for (var i = 0; i < questionCount; i++)
        {
            var question = FromArray(message, offset, out offset);
            questions.Add(question);
        }

        endOffset = offset;
        return questions;
    }

    public static Question FromArray(byte[] message, int offset)
    {
        return FromArray(message, offset, out _);
    }

    public static Question FromArray(byte[] message, int offset, out int endOffset)
    {
        var domain = Domain.FromArray(message, offset, out offset);
        var tail = Struct.GetStruct<Tail>(message, offset, Tail.Size);

        endOffset = offset + Tail.Size;

        return new Question(domain, tail.Type, tail.Class);
    }

    public Domain Name { get; } = domain;

    public RecordType Type { get; } = type;

    public RecordClass Class { get; } = klass;

    public int Size => Name.Size + Tail.Size;

    public byte[] ToArray()
    {
        var result = new byte[Size];
        WriteTo(result);
        return result;
    }

    public void WriteTo(Span<byte> destination)
    {
        Name.WriteTo(destination);

        var tail = new Tail
        {
            Type = Type,
            Class = Class,
        };
        Struct.GetBytes(tail, destination[Name.Size..]);
    }

    public override string ToString()
    {
        return ObjectStringifier.New(this)
            .Add(nameof(Name), nameof(Type), nameof(Class))
            .ToString();
    }

    [Endian(Endianness.Big)]
    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    private struct Tail
    {
        public const int Size = 4;

        private ushort _type;
        private ushort _class;

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
    }
}
