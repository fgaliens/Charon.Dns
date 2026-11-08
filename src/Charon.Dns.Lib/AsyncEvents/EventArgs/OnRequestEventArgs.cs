using System.Net;
using Charon.Dns.Lib.Protocol;
using Charon.Dns.Lib.Tracing;

namespace Charon.Dns.Lib.AsyncEvents;

public readonly record struct OnRequestEventArgs
{
    public required IRequest Request { get; init; }
    public required RequestTrace Trace { get; init; }
}
