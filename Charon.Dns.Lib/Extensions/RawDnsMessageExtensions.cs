using System;
using System.Buffers.Binary;

namespace Charon.Dns.Lib.Extensions;

public static class RawDnsMessageExtensions
{
    extension(Memory<byte> rawMessage)
    {
        public RawDnsMessage DnsMessage => new(rawMessage);
    }
    
    extension(byte[] rawMessage)
    {
        public RawDnsMessage DnsMessage => new(rawMessage);
    }
}

public readonly record struct RawDnsMessage(Memory<byte> RawMessage)
{
    public RawDnsMessageHeader Header => new(RawMessage);
}

public readonly record struct RawDnsMessageHeader(Memory<byte> RawMessage)
{
    public ushort Id 
    {
        get => RawMessage.ReadUint16(0);
        set => RawMessage.WriteUint16(0, value);
    }
}

file static class RawDataHelper
{
    extension(Memory<byte> rawMessage)
    {
        public ushort ReadUint16(int offset)
        {
            return BinaryPrimitives.ReadUInt16BigEndian(rawMessage[offset..(offset + 2)].Span);
        }
        
        public void WriteUint16(int offset, ushort value)
        {
            BinaryPrimitives.WriteUInt16BigEndian(rawMessage[offset..(offset + 2)].Span, value);
        }
    }
}
