using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Charon.Dns.Lib.Protocol;
using Charon.Dns.Lib.Tracing;
using Serilog;

namespace Charon.Dns.Lib.Client.RequestResolver;

/// <summary>
/// DNS over TLS client (RFC 7858).
/// </summary>
/// <remarks>
/// TODO: not implemented. Requirements:
/// <list type="bullet">
/// <item>RFC 7858, 3.1: the default port is 853; port 53 MUST NOT be used for DNS over TLS.</item>
/// <item>RFC 7858, 3.2; BCP 195 (RFC 9325): the TLS handshake is done before any DNS message;
/// TLS 1.2 is the minimum, TLS 1.3 is preferred. ALPN "dot" MAY be offered.</item>
/// <item>RFC 8310, 5 and 8 (Strict privacy profile): the server certificate MUST be validated
/// against <c>host</c> (a DNS name or an IP address SAN); on failure the query MUST fail, without
/// falling back to plain DNS. SNI is sent when <c>host</c> is a DNS name.</item>
/// <item>RFC 7858, 3.3: messages are framed as over TCP: a 2-byte big-endian length prefix (RFC 1035, 4.2.2).</item>
/// <item>RFC 7858, 3.3: queries SHOULD be pipelined over one connection, responses may come out of order
/// and MUST be matched by ID (an internal ID like in <see cref="UdpRequestResolver"/>) and question.</item>
/// <item>RFC 7858, 3.4: the connection SHOULD be kept open and reused; it is reopened when the server closes it,
/// pending queries are retried. TLS session resumption (RFC 8446, 2.2) SHOULD be used on reconnect.</item>
/// <item>RFC 7830; RFC 8467, 4.1: queries SHOULD be padded with the EDNS(0) Padding option to a multiple of 128 bytes.</item>
/// <item>Handshake and query timeouts are applied; an idle connection is closed by the client (RFC 7766, 6.2.2).</item>
/// </list>
/// </remarks>
public sealed class DotRequestResolver(IPEndPoint dnsEndpoint, string host, ILogger logger) : IRequestResolver
{
    public Task<IResponse> Resolve(IRequest request, RequestTrace trace, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException($"DNS over TLS client for {dnsEndpoint} ({host}) is not implemented yet");
    }
}
