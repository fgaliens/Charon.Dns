using System;

namespace Charon.Dns.Lib.Protocol
{
    public interface IMessageEntry
    {
        Domain Name { get; }
        RecordType Type { get; }
        RecordClass Class { get; }

        int Size { get; }
        byte[] ToArray();

        void WriteTo(Span<byte> destination) => ToArray().AsSpan().CopyTo(destination);
    }
}
