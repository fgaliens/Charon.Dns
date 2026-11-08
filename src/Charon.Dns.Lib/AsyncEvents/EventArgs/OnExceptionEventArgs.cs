using System;
using Charon.Dns.Lib.Tracing;

namespace Charon.Dns.Lib.AsyncEvents;

public readonly record struct OnExceptionEventArgs
{
    public required Exception Exception { get; init; }
    public required RequestTrace? Trace { get; init; }
}
