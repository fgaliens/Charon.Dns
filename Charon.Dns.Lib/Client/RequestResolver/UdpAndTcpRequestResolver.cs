using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Charon.Dns.Lib.Protocol;
using Charon.Dns.Lib.Tracing;
using Charon.Dns.Utils.Units;
using Serilog;

namespace Charon.Dns.Lib.Client.RequestResolver;

/// <summary>
/// Plain DNS: UDP, with TCP for responses that don't fit into a datagram.
/// </summary>
/// <remarks>
/// TODO: only UDP is used for now. Requirements for the TCP fallback:
/// <list type="bullet">
/// <item>RFC 1035, 4.2.1; RFC 2181, 9; RFC 7766, 5: a response with the TC flag set MUST be retried over TCP,
/// the truncated response must not be returned or cached.</item>
/// <item>RFC 6891, 6.2.5; DNS Flag Day 2020: the advertised EDNS UDP payload size SHOULD be 1232 bytes,
/// so large responses are truncated instead of being fragmented.</item>
/// <item>RFC 7766, 6.2.1: a client SHOULD reuse a TCP connection for several queries
/// and SHOULD pipeline them without waiting for previous responses.</item>
/// <item>RFC 7766, 6.2.1.1: responses may come out of order, they MUST be matched by ID and question.</item>
/// <item>RFC 7766, 6.2.2: an idle connection SHOULD be closed by the client; a connection closed
/// by the server must be reopened, and queries without responses retried on it.</item>
/// <item>RFC 7766, 8: the length prefix and the message SHOULD be sent in a single write.</item>
/// <item>The TCP query is limited by the same timeout as the UDP one.</item>
/// </list>
/// </remarks>
public sealed class UdpAndTcpRequestResolver : IRequestResolver, IDisposable
{
    private readonly UdpRequestResolver _udpResolver;
    private readonly TcpRequestResolver _tcpResolver;

    public UdpAndTcpRequestResolver(IPEndPoint dnsEndpoint, ByteUnit? socketBufferSize, ILogger logger)
    {
        _udpResolver = new UdpRequestResolver(dnsEndpoint, socketBufferSize, logger);
        _tcpResolver = new TcpRequestResolver(dnsEndpoint);
    }

    public Task<IResponse> Resolve(IRequest request, RequestTrace trace, CancellationToken cancellationToken = default)
    {
        return _udpResolver.Resolve(request, trace, cancellationToken);
    }

    public void Dispose()
    {
        _udpResolver.Dispose();
    }
}
