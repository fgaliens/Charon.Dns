namespace Charon.Dns.Settings;

/// <summary>
/// Transport of DNS messages, used both for listening and for upstream servers.
/// </summary>
public enum TransportMode
{
    /// <summary>
    /// Plain DNS: UDP, with TCP for large messages (RFC 1035, RFC 7766).
    /// </summary>
    UdpAndTcp,

    /// <summary>
    /// DNS over TLS (RFC 7858).
    /// </summary>
    DoT,

    /// <summary>
    /// DNS over HTTPS (RFC 8484).
    /// </summary>
    DoH,
}

public static class TransportModeExtensions
{
    extension(TransportMode mode)
    {
        public int DefaultPort => mode switch
        {
            TransportMode.UdpAndTcp => 53,
            // RFC 7858, 3.1
            TransportMode.DoT => 853,
            TransportMode.DoH => 443,
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
        };
    }
}
