using System.Net;
using Serilog;

namespace Charon.Dns.Lib.Tracing;

public record RequestTrace
{
    public static RequestTrace Empty { get; } = new()
    {
        Id = 0,
        RemoteEndPoint = new IPEndPoint(0, 0),
        Logger = Serilog.Core.Logger.None,
    };

    public required ulong Id { get; init; }
    public required IPEndPoint RemoteEndPoint { get; init; }
    public required ILogger Logger { get; init; }
}
