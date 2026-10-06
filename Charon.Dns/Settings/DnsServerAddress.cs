using System.Diagnostics.CodeAnalysis;
using System.Net;

namespace Charon.Dns.Settings;

public readonly record struct DnsServerAddress(IPEndPoint EndPoint) : IParsable<DnsServerAddress>
{
    private const int DefaultPort = 53;

    public static DnsServerAddress Parse(string s, IFormatProvider? provider)
    {
        if (!TryParse(s, provider, out var result))
        {
            throw new FormatException($"Unable to parse '{s}' as a DNS server address");
        }

        return result;
    }

    public static bool TryParse(
        [NotNullWhen(true)] string? s, IFormatProvider? provider, out DnsServerAddress result)
    {
        result = default;

        if (s is null)
        {
            return false;
        }

        // Must be checked before IPEndPoint.TryParse: that call also accepts a bare
        // address (defaulting the port to 0), which would mask the intended default port.
        if (IPAddress.TryParse(s, out var address))
        {
            result = new DnsServerAddress(new IPEndPoint(address, DefaultPort));
            return true;
        }

        if (IPEndPoint.TryParse(s, out var endPoint))
        {
            result = new DnsServerAddress(endPoint);
            return true;
        }

        return false;
    }
}
