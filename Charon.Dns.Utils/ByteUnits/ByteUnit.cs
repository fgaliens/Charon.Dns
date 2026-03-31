using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Charon.Dns.Utils.ByteUnits;

public readonly struct ByteUnit(uint bytes) : IParsable<ByteUnit>
{
    private static readonly Regex StringValueParser = 
        new(@"^(\d+)\s*(b|kb|mb|gb)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    
    public int Bytes => (int)bytes;
    public int Kilobytes => Bytes / 1024;
    public int Megabytes => Kilobytes / 1024;
    public int Gigabytes => Megabytes / 1024;
    
    public static ByteUnit Parse(string s, IFormatProvider? provider)
    {
        if (TryRead(s, out var unit, out var exception))
        {
            return unit.Value;;
        }

        throw exception;
    }

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out ByteUnit result)
    {
        result = default;
        
        if (s is null)
        {
            return false;
        }

        if (TryRead(s, out var unit, out _))
        {
            result = unit.Value;
            return true;
        }
        
        return false;
    }
    
    private static bool TryRead(
        string stringValue, 
        [NotNullWhen(true)] out ByteUnit? value, 
        [NotNullWhen(false)] out Exception? exception)
    {
        value = null;
        exception = null;
        
        var match = StringValueParser.Match(stringValue);
        if (match is { Success: true, Groups.Count: 3 })
        {
            var size = uint.Parse(match.Groups[1].Value);
            var metric = match.Groups[2].Value.ToUpperInvariant();
            value = metric switch
            {
                "B" => new ByteUnit(size),
                "KB" => new ByteUnit(1024 * size),
                "MB" => new ByteUnit(1024 * 1024 * size),
                "GB" => new ByteUnit(1024 * 1024 * 1024 * size),
                _ => null,
            };

            if (value is not null)
            {
                return true;
            }
            
            exception = new InvalidOperationException($"Unexpected metric '{metric}'");
            return false;
        }

        exception = new InvalidDataException($"Unable to parse value '{value}'. It should match pattern '<Value> <Metric>' (ex. 10 Kb)");
        return false;
    }
}
