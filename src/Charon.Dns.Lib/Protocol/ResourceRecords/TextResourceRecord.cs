using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Charon.Dns.Lib.Protocol.ResourceRecords;

public partial class TextResourceRecord : BaseResourceRecord
{
    public TextResourceRecord(IResourceRecord record)
        : base(record)
    {
        TextData = CharacterString.GetAllFromArray(Data, 0);
    }

    public TextResourceRecord(Domain domain, IList<CharacterString> characterStrings, TimeSpan ttl = default)
        : base(Create(domain, characterStrings, ttl))
    {
        TextData = new ReadOnlyCollection<CharacterString>(characterStrings);
    }

    public TextResourceRecord(Domain domain, string attributeName, string attributeValue, TimeSpan ttl = default)
        : this(domain, FormatAttributeNameValue(attributeName, attributeValue), ttl)
    {
    }

    public IList<CharacterString> TextData { get; }

    public KeyValuePair<string?, string> Attribute
    {
        get
        {
            var text = ToStringTextData();
            var match = TextRecordPattern().Match(text);

            if (!match.Success)
            {
                return new KeyValuePair<string?, string>(null, Unescape(text));
            }

            var attributeName = match.Groups[1].Length > 0
                ? Unescape(Trim(match.Groups[1].Value))
                : null;
            var attributeValue = Unescape(match.Groups[2].Value);
            return new KeyValuePair<string?, string>(attributeName, attributeValue);
        }
    }

    public string ToStringTextData()
    {
        return ToStringTextData(Encoding.ASCII);
    }

    public string ToStringTextData(Encoding encoding)
    {
        return string.Concat(TextData.Select(c => c.ToString(encoding)));
    }

    public override string ToString()
    {
        var textData = ToStringTextData();
        return Stringify().Add(nameof(TextData), (object)textData).ToString();
    }

    /// Regular expression that matches the attribute name/value.
    /// The first unescaped equal sign is the name/value delimiter.
    [GeneratedRegex(@"^([ -~]*?)(?<!`)=([ -~]*)$")]
    private static partial Regex TextRecordPattern();

    /// Regular expression that matches unescaped leading/trailing whitespace.
    [GeneratedRegex(@"^\s+|((?<!`)\s)+$")]
    private static partial Regex TrimNamePattern();

    /// Regular expression that matches unescaped characters.
    [GeneratedRegex("([`=])")]
    private static partial Regex EscapePattern();

    /// Regular expression that matches escaped characters.
    [GeneratedRegex(@"`([`=\s])")]
    private static partial Regex UnescapePattern();

    private static string Trim(string value) => TrimNamePattern().Replace(value, string.Empty);
    private static string Escape(string value) => EscapePattern().Replace(value, "`$1");
    private static string Unescape(string value) => UnescapePattern().Replace(value, "$1");

    private static ResourceRecord Create(Domain domain, IList<CharacterString> characterStrings, TimeSpan ttl)
    {
        var data = new byte[characterStrings.Sum(c => c.Size)];
        var offset = 0;

        foreach (var characterString in characterStrings)
        {
            characterString.WriteTo(data.AsSpan(offset, characterString.Size));
            offset += characterString.Size;
        }

        return new ResourceRecord(domain, data, RecordType.TXT, RecordClass.IN, ttl);
    }

    private static IList<CharacterString> FormatAttributeNameValue(string attributeName, string attributeValue)
    {
        return CharacterString.FromString($"{Escape(attributeName)}={attributeValue}");
    }
}
