using System.Buffers.Binary;

namespace Charon.Dns.Extensions;

public static class UInt128Utils
{
    public static UInt128 Create(ReadOnlySpan<byte> bytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bytes.Length);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(bytes.Length, 16);

        return BinaryPrimitives.ReadUInt128LittleEndian(bytes);
    }

    public static byte ReadByte(this in UInt128 value, int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, 16);

        Span<byte> buffer = stackalloc byte[16];
        BinaryPrimitives.WriteUInt128LittleEndian(buffer, value);
        return buffer[index];
    }
}