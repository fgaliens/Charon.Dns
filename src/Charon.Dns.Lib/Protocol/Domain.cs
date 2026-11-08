using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Text;
using Charon.Dns.Lib.Protocol.Utils;

namespace Charon.Dns.Lib.Protocol;

public class Domain : IEquatable<Domain>, IComparable<Domain>
{
    private const byte AsciiUppercaseFirst = 65;
    private const byte AsciiUppercaseLast = 90;
    private const byte AsciiLowercaseFirst = 97;
    private const byte AsciiLowercaseLast = 122;
    private const byte AsciiUppercaseMask = 223;
    private const string HexDigits = "0123456789abcdef";

    // RFC 1035, 2.3.4
    public const int MaxLabelLength = 63;
    public const int MaxNameSize = 255;

    private readonly byte[][] _labels;

    public static Domain FromString(string domain)
    {
        return new Domain(domain);
    }

    public static Domain FromArray(byte[] message, int offset)
    {
        return FromArray(message, offset, out _);
    }

    public static Domain FromArray(byte[] message, int offset, out int endOffset)
    {
        var labels = new List<byte[]>();
        var endOffsetAssigned = false;
        endOffset = 0;
        byte lengthOrPointer;
        var visitedOffsetPointers = new HashSet<int>();

        while ((lengthOrPointer = message[offset++]) > 0)
        {
            // Two highest bits are set (pointer)
            if (lengthOrPointer.GetBitValueAt(6, 2) == 3)
            {
                if (!endOffsetAssigned)
                {
                    endOffsetAssigned = true;
                    endOffset = offset + 1;
                }

                var pointer = lengthOrPointer.GetBitValueAt(0, 6);
                offset = (pointer << 8) | message[offset];

                if (!visitedOffsetPointers.Add(offset))
                {
                    throw new ArgumentException("Compression pointer loop detected");
                }

                continue;
            }

            if (lengthOrPointer.GetBitValueAt(6, 2) != 0)
            {
                throw new ArgumentException("Unexpected bit pattern in label length");
            }

            var label = new byte[lengthOrPointer];
            Array.Copy(message, offset, label, 0, lengthOrPointer);
            labels.Add(label);

            offset += lengthOrPointer;
        }

        if (!endOffsetAssigned)
        {
            endOffset = offset;
        }

        return new Domain(labels.ToArray());
    }

    public static Domain PointerName(IPAddress ip)
    {
        var reverseIp = FormatReverseIp(ip);
        return new Domain(reverseIp);
    }

    private static string FormatReverseIp(IPAddress ip)
    {
        Span<byte> address = stackalloc byte[16];
        ip.TryWriteBytes(address, out var written);

        if (written == 4)
        {
            return $"{address[3]}.{address[2]}.{address[1]}.{address[0]}.in-addr.arpa";
        }

        // Every byte is written as two nibbles, the lowest one first
        var result = new StringBuilder();
        for (var i = written - 1; i >= 0; i--)
        {
            var b = address[i];
            result
                .Append(HexDigits[b.GetBitValueAt(0, 4)])
                .Append('.')
                .Append(HexDigits[b.GetBitValueAt(4, 4)])
                .Append('.');
        }

        return result.Append("ip6.arpa").ToString();
    }

    private static bool IsAsciiAlphabet(byte b)
    {
        return b is >= AsciiUppercaseFirst and <= AsciiUppercaseLast
            or >= AsciiLowercaseFirst and <= AsciiLowercaseLast;
    }

    private static int CompareTo(byte a, byte b)
    {
        if (IsAsciiAlphabet(a) && IsAsciiAlphabet(b))
        {
            a &= AsciiUppercaseMask;
            b &= AsciiUppercaseMask;
        }

        return a - b;
    }

    private static int CompareTo(byte[] a, byte[] b)
    {
        var length = Math.Min(a.Length, b.Length);

        for (var i = 0; i < length; i++)
        {
            var result = CompareTo(a[i], b[i]);
            if (result != 0)
            {
                return result;
            }
        }

        return a.Length - b.Length;
    }

    public Domain(byte[][] labels)
    {
        Validate(labels);
        _labels = labels;
    }

    public Domain(string[] labels, Encoding encoding)
        : this(labels.Select(encoding.GetBytes).ToArray())
    {
    }

    /// <summary>
    /// "" and "." are the root, a trailing dot of an absolute name is ignored.
    /// </summary>
    public Domain(string domain) : this(SplitLabels(domain)) { }

    public Domain(string[] labels) : this(labels, Encoding.ASCII) { }

    private static string[] SplitLabels(string domain)
    {
        if (domain is "" or ".")
        {
            return [];
        }

        var relativeName = domain.EndsWith('.') ? domain[..^1] : domain;
        return relativeName.Split('.');
    }

    private static void Validate(byte[][] labels)
    {
        // Every label is prefixed by its length, the name ends with the zero length root label
        var size = 1;

        foreach (var label in labels)
        {
            size += label.Length switch
            {
                0 => throw new ArgumentException("Domain name contains an empty label"),
                > MaxLabelLength => throw new ArgumentException($"Domain name label is longer than {MaxLabelLength} bytes"),
                _ => label.Length + 1
            };
        }

        if (size > MaxNameSize)
        {
            throw new ArgumentException($"Domain name is longer than {MaxNameSize} bytes");
        }
    }

    public bool TryGetParent([NotNullWhen(true)] out Domain? parent)
    {
        if (_labels.Length <= 1)
        {
            parent = null;
            return false;
        }

        parent = new Domain(_labels[1..]);
        return true;
    }

    public int Size => _labels.Sum(label => label.Length) + _labels.Length + 1;

    public byte[] ToArray()
    {
        var result = new byte[Size];
        WriteTo(result);
        return result;
    }

    public void WriteTo(Span<byte> destination)
    {
        var offset = 0;

        foreach (var label in _labels)
        {
            destination[offset++] = (byte)label.Length;
            label.AsSpan().CopyTo(destination[offset..]);
            offset += label.Length;
        }

        destination[offset] = 0;
    }

    public string ToString(Encoding encoding)
    {
        return string.Join('.', _labels.Select(encoding.GetString));
    }

    public override string ToString()
    {
        return ToString(Encoding.ASCII);
    }

    public int CompareTo(Domain? other)
    {
        if (other is null)
        {
            return 1;
        }

        var length = Math.Min(_labels.Length, other._labels.Length);

        for (var i = 0; i < length; i++)
        {
            var result = CompareTo(_labels[i], other._labels[i]);
            if (result != 0)
            {
                return result;
            }
        }

        return _labels.Length - other._labels.Length;
    }

    public override bool Equals(object? obj)
    {
        return obj is Domain domain && Equals(domain);
    }

    public bool Equals(Domain? other)
    {
        return other is not null && CompareTo(other) == 0;
    }

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;

            foreach (var label in _labels)
            {
                foreach (var b in label)
                {
                    hash = hash * 31 + (IsAsciiAlphabet(b) ? b & AsciiUppercaseMask : b);
                }
            }

            return hash;
        }
    }
}
