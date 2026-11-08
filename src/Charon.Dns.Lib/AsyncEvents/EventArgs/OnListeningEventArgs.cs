using Charon.Dns.Lib.Server;

namespace Charon.Dns.Lib.AsyncEvents;

public readonly record struct OnListeningEventArgs
{
    public required DnsServer Sender { get; init; }
}
