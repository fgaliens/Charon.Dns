using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using Charon.Dns.Lib.Protocol.Utils;

namespace Charon.Dns.Lib.Protocol
{
    public class Domain : IEquatable<Domain>, IComparable<Domain>
    {
        private const byte AsciiUppercaseFirst = 65;
        private const byte AsciiUppercaseLast = 90;
        private const byte AsciiLowercaseFirst = 97;
        private const byte AsciiLowercaseLast = 122;
        private const byte AsciiUppercaseMask = 223;

        private readonly byte[][] _labels;

        public static Domain FromString(string domain)
        {
            return new Domain(domain);
        }

        public static Domain FromArray(byte[] message, int offset)
        {
            return FromArray(message, offset, out offset);
        }

        public static Domain FromArray(byte[] message, int offset, out int endOffset)
        {
            IList<byte[]> labels = new List<byte[]>();
            bool endOffsetAssigned = false;
            endOffset = 0;
            byte lengthOrPointer;
            HashSet<int> visitedOffsetPointers = new HashSet<int>();

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

                    ushort pointer = lengthOrPointer.GetBitValueAt(0, 6);
                    offset = (pointer << 8) | message[offset];

                    if (visitedOffsetPointers.Contains(offset))
                    {
                        throw new ArgumentException("Compression pointer loop detected");
                    }
                    visitedOffsetPointers.Add(offset);

                    continue;
                }

                if (lengthOrPointer.GetBitValueAt(6, 2) != 0)
                {
                    throw new ArgumentException("Unexpected bit pattern in label length");
                }

                byte length = lengthOrPointer;
                byte[] label = new byte[length];
                Array.Copy(message, offset, label, 0, length);

                labels.Add(label);

                offset += length;
            }

            if (!endOffsetAssigned)
            {
                endOffset = offset;
            }

            return new Domain(labels.ToArray());
        }

        public static Domain PointerName(IPAddress ip)
        {
            return new Domain(FormatReverseIp(ip));
        }

        private static string FormatReverseIp(IPAddress ip)
        {
            byte[] address = ip.GetAddressBytes();

            if (address.Length == 4)
            {
                return string.Join(".", address.Reverse().Select(b => b.ToString())) + ".in-addr.arpa";
            }

            byte[] nibbles = new byte[address.Length * 2];

            for (int i = 0, j = 0; i < address.Length; i++, j = 2 * i)
            {
                byte b = address[i];

                nibbles[j] = b.GetBitValueAt(4, 4);
                nibbles[j + 1] = b.GetBitValueAt(0, 4);
            }

            return string.Join(".", nibbles.Reverse().Select(b => b.ToString("x"))) + ".ip6.arpa";
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
            int length = Math.Min(a.Length, b.Length);

            for (int i = 0; i < length; i++)
            {
                int v = CompareTo(a[i], b[i]);
                if (v != 0) return v;
            }

            return a.Length - b.Length;
        }

        public Domain(byte[][] labels)
        {
            _labels = labels;
        }

        public Domain(string[] labels, Encoding encoding)
        {
            _labels = labels.Select(label => encoding.GetBytes(label)).ToArray();
        }

        public Domain(string domain) : this(domain.Split('.')) { }

        public Domain(string[] labels) : this(labels, Encoding.ASCII) { }

        public int Size
        {
            get { return _labels.Sum(l => l.Length) + _labels.Length + 1; }
        }

        public byte[] ToArray()
        {
            byte[] result = new byte[Size];
            int offset = 0;

            foreach (byte[] label in _labels)
            {
                result[offset++] = (byte)label.Length;
                label.CopyTo(result, offset);
                offset += label.Length;
            }

            result[offset] = 0;
            return result;
        }

        public string ToString(Encoding encoding)
        {
            return string.Join(".", _labels.Select(label => encoding.GetString(label)));
        }

        public override string ToString()
        {
            return ToString(Encoding.ASCII);
        }

        public int CompareTo(Domain other)
        {
            int length = Math.Min(_labels.Length, other._labels.Length);

            for (int i = 0; i < length; i++)
            {
                int v = CompareTo(_labels[i], other._labels[i]);
                if (v != 0) return v;
            }

            return _labels.Length - other._labels.Length;
        }

        public override bool Equals(object obj)
        {
            if (obj is Domain domain)
            {
                return Equals(domain);
            }

            return false;
        }
        
        public bool Equals(Domain other)
        {
            if (other is null)
            {
                return false;
            }

            return CompareTo(other) == 0;
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;

                foreach (byte[] label in _labels)
                {
                    foreach (byte b in label)
                    {
                        hash = hash * 31 + (IsAsciiAlphabet(b) ? b & AsciiUppercaseMask : b);
                    }
                }

                return hash;
            }
        }
    }
}
