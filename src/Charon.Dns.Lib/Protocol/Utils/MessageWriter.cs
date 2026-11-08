using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using Charon.Dns.Lib.Protocol.ResourceRecords;

namespace Charon.Dns.Lib.Protocol.Utils;

/// <summary>
/// Writes a message with domain name compression (RFC 1035, 4.1.4).
/// A name is replaced by a pointer to the same, already written suffix.
/// </summary>
internal ref struct MessageWriter
{
    private const int MaxPointerOffset = 0x3FFF;
    private const byte PointerMask = 0xC0;
    private const int PointerSize = 2;
    private const int StartOfAuthorityOptionsSize = 20;

    // Suffix hash -> offset of the first written name with it.
    // A collision of different suffixes only means that the name is written uncompressed.
    [ThreadStatic]
    private static Dictionary<int, int>? _cachedSuffixOffsets;

    private readonly Span<byte> _destination;
    private readonly Dictionary<int, int> _suffixOffsets;
    private int _position;

    public MessageWriter(Span<byte> destination)
    {
        _destination = destination;
        _suffixOffsets = _cachedSuffixOffsets ?? [];
        _cachedSuffixOffsets = null;
        _suffixOffsets.Clear();
        _position = 0;
    }

    public int Position => _position;

    /// <summary>
    /// Returns the dictionary for the next message on this thread.
    /// </summary>
    public void Dispose()
    {
        _cachedSuffixOffsets = _suffixOffsets;
    }

    public void WriteHeader(Header header)
    {
        header.WriteTo(_destination.Slice(_position, Header.HeaderSize));
        _position += Header.HeaderSize;
    }

    public void WriteQuestion(Question question)
    {
        WriteDomain(question.Name);
        WriteUInt16((ushort)question.Type);
        WriteUInt16((ushort)question.Class);
    }

    public void WriteRecord(IResourceRecord record)
    {
        WriteDomain(record.Name);
        WriteUInt16((ushort)record.Type);
        WriteUInt16((ushort)record.Class);
        WriteUInt32((uint)record.TimeToLive.TotalSeconds);

        var dataLengthPosition = _position;
        _position += PointerSize;
        WriteRecordData(record);

        var dataLength = _position - dataLengthPosition - PointerSize;
        BinaryPrimitives.WriteUInt16BigEndian(_destination[dataLengthPosition..], (ushort)dataLength);
    }

    /// <summary>
    /// Only names in record data of RFC 1035 types may be compressed (RFC 3597, 4).
    /// Data of these types always holds uncompressed names, it is rebuilt on parsing.
    /// </summary>
    private void WriteRecordData(IResourceRecord record)
    {
        ReadOnlySpan<byte> data = record.Data;

        switch (record.Type)
        {
            case RecordType.CNAME:
            case RecordType.NS:
            case RecordType.PTR:
                if (TryGetNameSize(data, out var nameSize) && nameSize == data.Length)
                {
                    WriteName(data);
                    return;
                }
                break;
            case RecordType.MX:
                if (data.Length > PointerSize
                    && TryGetNameSize(data[PointerSize..], out var exchangeSize)
                    && PointerSize + exchangeSize == data.Length)
                {
                    WriteBytes(data[..PointerSize]);
                    WriteName(data[PointerSize..]);
                    return;
                }
                break;
            case RecordType.SOA:
                if (TryGetNameSize(data, out var masterSize)
                    && TryGetNameSize(data[masterSize..], out var responsibleSize)
                    && masterSize + responsibleSize + StartOfAuthorityOptionsSize == data.Length)
                {
                    WriteName(data[..masterSize]);
                    WriteName(data.Slice(masterSize, responsibleSize));
                    WriteBytes(data[^StartOfAuthorityOptionsSize..]);
                    return;
                }
                break;
        }

        WriteBytes(data);
    }

    private void WriteDomain(Domain domain)
    {
        Span<byte> name = stackalloc byte[Domain.MaxNameSize];
        var nameSize = domain.Size;
        domain.WriteTo(name[..nameSize]);
        WriteName(name[..nameSize]);
    }

    /// <param name="name">Uncompressed name in wire format, ends with the root label</param>
    private void WriteName(scoped ReadOnlySpan<byte> name)
    {
        var labelOffset = 0;

        while (name[labelOffset] != 0)
        {
            var suffix = name[labelOffset..];
            var suffixHash = GetHashCode(suffix);

            if (_suffixOffsets.TryGetValue(suffixHash, out var suffixOffset)
                && IsNameAt(suffixOffset, suffix))
            {
                var pointer = (ushort)(PointerMask << 8 | suffixOffset);
                WriteUInt16(pointer);
                return;
            }

            if (_position <= MaxPointerOffset)
            {
                _suffixOffsets.TryAdd(suffixHash, _position);
            }

            var labelSize = name[labelOffset] + 1;
            WriteBytes(name.Slice(labelOffset, labelSize));
            labelOffset += labelSize;
        }

        _destination[_position++] = 0;
    }

    /// <summary>
    /// Compares the name written at the offset (it may contain pointers) with the uncompressed one.
    /// Labels are compared case-sensitively, so the case of every written name is kept.
    /// </summary>
    private readonly bool IsNameAt(int offset, scoped ReadOnlySpan<byte> name)
    {
        var nameOffset = 0;

        // Pointers of a written name always go backwards, so the loop ends
        while (true)
        {
            var lengthOrPointer = _destination[offset];
            if ((lengthOrPointer & PointerMask) == PointerMask)
            {
                offset = BinaryPrimitives.ReadUInt16BigEndian(_destination[offset..]) & MaxPointerOffset;
                continue;
            }

            var labelSize = lengthOrPointer + 1;
            if (nameOffset + labelSize > name.Length
                || !_destination.Slice(offset, labelSize).SequenceEqual(name.Slice(nameOffset, labelSize)))
            {
                return false;
            }

            if (lengthOrPointer == 0)
            {
                return true;
            }

            offset += labelSize;
            nameOffset += labelSize;
        }
    }

    private static bool TryGetNameSize(ReadOnlySpan<byte> data, out int size)
    {
        size = 0;

        while (size < data.Length && size < Domain.MaxNameSize)
        {
            var length = data[size];
            if (length == 0)
            {
                size++;
                return true;
            }

            if (length > Domain.MaxLabelLength)
            {
                return false;
            }

            size += length + 1;
        }

        return false;
    }

    private static int GetHashCode(ReadOnlySpan<byte> suffix)
    {
        var hashCode = new HashCode();
        hashCode.AddBytes(suffix);
        return hashCode.ToHashCode();
    }

    private void WriteBytes(scoped ReadOnlySpan<byte> bytes)
    {
        bytes.CopyTo(_destination[_position..]);
        _position += bytes.Length;
    }

    private void WriteUInt16(ushort value)
    {
        BinaryPrimitives.WriteUInt16BigEndian(_destination[_position..], value);
        _position += sizeof(ushort);
    }

    private void WriteUInt32(uint value)
    {
        BinaryPrimitives.WriteUInt32BigEndian(_destination[_position..], value);
        _position += sizeof(uint);
    }
}
