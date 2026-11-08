using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace Charon.Dns.Lib.Client.RequestResolver;

/// <summary>
/// DNS over TCP client (RFC 7766).
/// </summary>
public sealed class TcpRequestResolver : StreamRequestResolver
{
    // RFC 7766 has no timeout, the same one as for UDP is used
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(2);

    public TcpRequestResolver(IPEndPoint dnsEndpoint, ILogger logger)
        : base(dnsEndpoint, QueryTimeout, logger)
    {
        logger.Information("TCP request resolver for DNS {DNS} created", dnsEndpoint);
    }

    protected override string Transport => "TCP";

    protected override async Task<Stream> Connect(CancellationToken cancellationToken)
    {
        var socket = await ConnectSocket(cancellationToken);
        return new NetworkStream(socket, ownsSocket: true);
    }
}
