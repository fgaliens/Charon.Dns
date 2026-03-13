using System.Diagnostics.CodeAnalysis;
using System.Text;
using Charon.Dns.Extensions;

namespace Charon.Dns.Net;

public readonly struct IpV4Network : IEquatable<IpV4Network>
{
    private const byte MaxSubnetSize = 32;
    private readonly uint _ip;
    private readonly byte _subnetSize;

    public IpV4Network(ReadOnlyMemory<byte> ip, byte subnetSize)
    {
        ArgumentOutOfRangeException.ThrowIfNotEqual(ip.Length, 4);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(subnetSize, MaxSubnetSize);

        _ip = BitConverter.ToUInt32(ip.Span);
        _subnetSize = subnetSize;
    }

    private IpV4Network(uint ip, byte subnetSize)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(subnetSize, MaxSubnetSize);

        _ip = ip;
        _subnetSize = subnetSize;
    }

    public IpV4Network SubnetMask => new(GetSubnetMask(), MaxSubnetSize);

    public IpV4Network MinAddress => new(_ip & GetSubnetMask(), _subnetSize);

    public IpV4Network MaxAddress => new(_ip | ~GetSubnetMask(), _subnetSize);

    public override int GetHashCode()
    {
        return HashCode.Combine(_ip, _subnetSize);
    }

    public bool Equals(IpV4Network otherAddress)
    {
        return _ip == otherAddress._ip && _subnetSize == otherAddress._subnetSize;
    }

    public override bool Equals([NotNullWhen(true)] object? obj)
    {
        if (obj is IpV4Network ip)
        {
            return Equals(ip);
        }

        return false;
    }

    public override string ToString()
    {
        Span<byte> span = stackalloc byte[4];
        FillIpIntoSpan(span);
        if (_subnetSize != MaxSubnetSize)
        {
            return $"{span[0]}.{span[1]}.{span[2]}.{span[3]}/{_subnetSize}";
        }

        return $"{span[0]}.{span[1]}.{span[2]}.{span[3]}";
    }

    public void WriteToStringBuilder(StringBuilder stringBuilder, bool alwaysAddSubnetSize = true)
    {
        const int expectedLength = 15 + 4;
        stringBuilder.EnsureCapacity(stringBuilder.Length + expectedLength);

        Span<byte> span = stackalloc byte[4];
        FillIpIntoSpan(span);

        var firstItem = true;
        foreach (var item in span)
        {
            if (!firstItem.SwitchToFalseIfTrue())
            {
                stringBuilder.Append('.');
            }

            stringBuilder.Append(item);
        }

        if (alwaysAddSubnetSize || _subnetSize != MaxSubnetSize)
        {
            stringBuilder
                .Append('/')
                .Append(_subnetSize);
        }
    }

    private void FillIpIntoSpan(Span<byte> dst)
    {
        BitConverter.TryWriteBytes(dst, _ip);
    }

    private uint GetSubnetMask()
    {
        return 0xFF_FF_FF_FF >> 32 - _subnetSize;
    }
}