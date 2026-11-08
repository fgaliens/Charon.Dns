using System;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using Charon.Dns.Lib.Protocol.Marshalling;
using Charon.Dns.Lib.Protocol.Utils;

namespace Charon.Dns.Lib.Protocol;

// 12 bytes message header
[Endian(Endianness.Big)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct Header
{
    public const int HeaderSize = 12;

    public static Header FromArray(byte[] header)
    {
        if (header.Length < HeaderSize)
        {
            throw new ArgumentException("Header length too small");
        }

        return Struct.GetStruct<Header>(header, 0, HeaderSize);
    }

    // Field layout is the wire format, the fields are serialized as is
    private ushort _id;

    private byte _flag0;
    private byte _flag1;

    // Question count: number of questions in the Question section
    private ushort _qdCount;

    // Answer record count: number of records in the Answer section
    private ushort _anCount;

    // Authority record count: number of records in the Authority section
    private ushort _nsCount;

    // Additional record count: number of records in the Additional section
    private ushort _arCount;

    public int Id
    {
        get => _id;
        set => _id = (ushort)value;
    }

    public int QuestionCount
    {
        get => _qdCount;
        set => _qdCount = (ushort)value;
    }

    public int AnswerRecordCount
    {
        get => _anCount;
        set => _anCount = (ushort)value;
    }

    public int AuthorityRecordCount
    {
        get => _nsCount;
        set => _nsCount = (ushort)value;
    }

    public int AdditionalRecordCount
    {
        get => _arCount;
        set => _arCount = (ushort)value;
    }

    public bool Response
    {
        get => Qr == 1;
        set => Qr = Convert.ToByte(value);
    }

    public OperationCode OperationCode
    {
        get => (OperationCode)Opcode;
        set => Opcode = (byte)value;
    }

    public bool AuthorativeServer
    {
        get => Aa == 1;
        set => Aa = Convert.ToByte(value);
    }

    public bool Truncated
    {
        get => Tc == 1;
        set => Tc = Convert.ToByte(value);
    }

    public bool RecursionDesired
    {
        get => Rd == 1;
        set => Rd = Convert.ToByte(value);
    }

    public bool RecursionAvailable
    {
        get => Ra == 1;
        set => Ra = Convert.ToByte(value);
    }

    public bool AuthenticData
    {
        get => Ad == 1;
        set => Ad = Convert.ToByte(value);
    }

    public bool CheckingDisabled
    {
        get => Cd == 1;
        set => Cd = Convert.ToByte(value);
    }

    public ResponseCode ResponseCode
    {
        get => (ResponseCode)RCode;
        set => RCode = (byte)value;
    }

    public readonly int Size => HeaderSize;

    public readonly byte[] ToArray()
    {
        var result = new byte[HeaderSize];
        WriteTo(result);
        return result;
    }

    public readonly void WriteTo(Span<byte> destination)
    {
        Struct.GetBytes(this, destination);
    }

    public override readonly string ToString()
    {
        return ObjectStringifier.New(this)
            .AddAll()
            .Remove(nameof(Size))
            .ToString();
    }

    internal readonly Vector128<byte> AsVector(bool withId = false)
    {
        Span<byte> buffer = stackalloc byte[Vector128<byte>.Count];
        if (withId)
        {
            BinaryPrimitives.WriteUInt16BigEndian(buffer[..2], _id);
        }
        buffer[2] = _flag0;
        buffer[3] = _flag1;
        BinaryPrimitives.WriteUInt16BigEndian(buffer[4..6], _qdCount);
        BinaryPrimitives.WriteUInt16BigEndian(buffer[6..8], _anCount);
        BinaryPrimitives.WriteUInt16BigEndian(buffer[8..10], _nsCount);
        BinaryPrimitives.WriteUInt16BigEndian(buffer[10..12], _arCount);

        return Vector128.Create(buffer);
    }

    // Query/Response Flag
    private byte Qr
    {
        readonly get => _flag0.GetBitValueAt(7, 1);
        set => _flag0 = _flag0.SetBitValueAt(7, 1, value);
    }

    // Operation Code
    private byte Opcode
    {
        readonly get => _flag0.GetBitValueAt(3, 4);
        set => _flag0 = _flag0.SetBitValueAt(3, 4, value);
    }

    // Authorative Answer Flag
    private byte Aa
    {
        readonly get => _flag0.GetBitValueAt(2, 1);
        set => _flag0 = _flag0.SetBitValueAt(2, 1, value);
    }

    // Truncation Flag
    private byte Tc
    {
        readonly get => _flag0.GetBitValueAt(1, 1);
        set => _flag0 = _flag0.SetBitValueAt(1, 1, value);
    }

    // Recursion Desired
    private byte Rd
    {
        readonly get => _flag0.GetBitValueAt(0, 1);
        set => _flag0 = _flag0.SetBitValueAt(0, 1, value);
    }

    // Recursion Available
    private byte Ra
    {
        readonly get => _flag1.GetBitValueAt(7, 1);
        set => _flag1 = _flag1.SetBitValueAt(7, 1, value);
    }

    // Authentic Data
    private byte Ad
    {
        readonly get => _flag1.GetBitValueAt(5, 1);
        set => _flag1 = _flag1.SetBitValueAt(5, 1, value);
    }

    // Checking Disabled
    private byte Cd
    {
        readonly get => _flag1.GetBitValueAt(4, 1);
        set => _flag1 = _flag1.SetBitValueAt(4, 1, value);
    }

    // Response Code
    private byte RCode
    {
        readonly get => _flag1.GetBitValueAt(0, 4);
        set => _flag1 = _flag1.SetBitValueAt(0, 4, value);
    }
}
