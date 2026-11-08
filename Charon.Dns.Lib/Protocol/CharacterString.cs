using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Charon.Dns.Lib.Protocol;

/// <summary>
/// Implementation of the "character-string" non-terminal as defined in
/// RFC1035 (chapter 3.3):
///   "character-string" is a single length octet followed by that number of
///    characters. "character-string" is treated as binary information, and
///    can be up to 256 characters in length (including the length octet).
/// </summary>
public class CharacterString
{
    private const int MaxSize = byte.MaxValue;

    private readonly byte[] _data;

    public static IList<CharacterString> GetAllFromArray(byte[] message, int offset)
    {
        return GetAllFromArray(message, offset, out _);
    }

    public static IList<CharacterString> GetAllFromArray(byte[] message, int offset, out int endOffset)
    {
        var characterStrings = new List<CharacterString>();

        while (offset < message.Length)
        {
            var characterString = FromArray(message, offset, out offset);
            characterStrings.Add(characterString);
        }

        endOffset = offset;
        return characterStrings;
    }

    public static CharacterString FromArray(byte[] message, int offset)
    {
        return FromArray(message, offset, out _);
    }

    public static CharacterString FromArray(byte[] message, int offset, out int endOffset)
    {
        if (message.Length < 1)
        {
            throw new ArgumentException("Empty message");
        }

        var length = message[offset++];
        var data = new byte[length];
        Array.Copy(message, offset, data, 0, length);
        endOffset = offset + length;
        return new CharacterString(data);
    }

    public static IList<CharacterString> FromString(string message)
    {
        return FromString(message, Encoding.ASCII);
    }

    public static IList<CharacterString> FromString(string message, Encoding encoding)
    {
        var bytes = encoding.GetBytes(message);
        var chunks = bytes.Chunk(MaxSize);
        var characterStrings = new List<CharacterString>();

        foreach (var chunk in chunks)
        {
            characterStrings.Add(new CharacterString(chunk));
        }

        return characterStrings;
    }

    public CharacterString(byte[] data)
    {
        _data = data.Length > MaxSize ? data[..MaxSize] : data;
    }

    public CharacterString(string data, Encoding encoding) : this(encoding.GetBytes(data)) { }

    public CharacterString(string data) : this(data, Encoding.ASCII) { }

    public int Size => _data.Length + 1;

    public byte[] ToArray()
    {
        var result = new byte[Size];
        WriteTo(result);
        return result;
    }

    public void WriteTo(Span<byte> destination)
    {
        destination[0] = (byte)_data.Length;
        _data.AsSpan().CopyTo(destination[1..]);
    }

    public string ToString(Encoding encoding)
    {
        return encoding.GetString(_data);
    }

    public override string ToString()
    {
        return ToString(Encoding.ASCII);
    }
}
