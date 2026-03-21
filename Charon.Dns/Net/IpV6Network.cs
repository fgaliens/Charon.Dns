using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;
using Charon.Dns.Extensions;

namespace Charon.Dns.Net;

public readonly struct IpV6Network : IIpNetwork<IpV6Network>
{
    private const byte MaxSubnetSize = 128;
    private readonly UInt128 _ip;
    private readonly byte _subnetSize;

    public IpV6Network(ReadOnlyMemory<byte> ip, byte subnetSize)
    {
        ArgumentOutOfRangeException.ThrowIfNotEqual(ip.Length, 16);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(subnetSize, MaxSubnetSize);

        _ip = UInt128Utils.Create(ip.Span);
        _subnetSize = subnetSize;
    }

    private IpV6Network(UInt128 ip, byte subnetSize)
    {
        _ip = ip;
        _subnetSize = subnetSize;
    }

    public IpV6Network SubnetMask => new(GetSubnetMask(), MaxSubnetSize);

    public IpV6Network MinAddress => new(_ip & GetSubnetMask(), _subnetSize);

    public IpV6Network MaxAddress => new(_ip | ~GetSubnetMask(), _subnetSize);

    public bool IsIpV4 { get; } = false;

    public bool IsIpV6 { get; } = true;
    
    public bool Equals(IpV6Network otherAddress)
    {
        return _ip == otherAddress._ip && _subnetSize == otherAddress._subnetSize;
    }

    public override bool Equals([NotNullWhen(true)] object? obj)
    {
        if (obj is IpV6Network ipNetwork)
        {
            return Equals(ipNetwork);
        }

        return false;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(_ip.GetHashCode(), _subnetSize);
    }

    private UInt128 GetSubnetMask()
    {
        return UInt128.MaxValue >> MaxSubnetSize - _subnetSize;
    }

    public override string ToString()
    {
        var stringBuilder = new StringBuilder();
        WriteToStringBuilder(stringBuilder);
        return stringBuilder.ToString();
    }

    public void WriteToStringBuilder(StringBuilder stringBuilder)
    {
        const int expectedLength = 39 + 4;
        stringBuilder.EnsureCapacity(stringBuilder.Length + expectedLength);

        for (var i = 0; i < Unsafe.SizeOf<UInt128>(); i += 2)
        {
            if (i > 0)
            {
                stringBuilder.Append(':');
            }

            var upper = _ip.ReadByte(i);
            var lower = _ip.ReadByte(i + 1);
            
            stringBuilder.Append($"{upper:x2}{lower:x2}");
        }
        
        stringBuilder
            .Append('/')
            .Append(_subnetSize);
    }
}