using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Charon.Dns.Lib.Protocol;
using Charon.Dns.Lib.Tracing;
using Serilog;

namespace Charon.Dns.Lib.Client.RequestResolver;

/// <summary>
/// DNS over HTTPS client (RFC 8484).
/// </summary>
/// <remarks>
/// TODO: not implemented. Requirements:
/// <list type="bullet">
/// <item>RFC 8484, 3: queries go to the URI template <c>https://{host}{path}</c>, the default port is 443.</item>
/// <item>The connection MUST go to <c>dnsEndpoint</c> (<c>SocketsHttpHandler.ConnectCallback</c>),
/// not to an address resolved by the system DNS, which may be this server itself.</item>
/// <item>RFC 8484, 4.1: a POST request carries the message in the body with
/// <c>Content-Type: application/dns-message</c>; a GET request carries it in the "dns" parameter
/// as base64url without padding (RFC 4648, 5). <c>Accept: application/dns-message</c> SHOULD be sent.</item>
/// <item>RFC 8484, 4.1: the DNS ID SHOULD be 0, so HTTP caches can reuse responses;
/// the original ID is restored in the response.</item>
/// <item>RFC 8484, 5.2: HTTP/2 is the recommended minimum (<c>HttpVersion.Version20</c>, ALPN "h2").</item>
/// <item>RFC 8484, 4.2.1: a non-2xx status or a Content-Type other than application/dns-message is a failure;
/// a successful status may still carry a DNS error (SERVFAIL, NXDOMAIN), which is returned as is.</item>
/// <item>The certificate MUST be validated against <c>host</c> (RFC 8310, 8; RFC 9110, 4.3.4).</item>
/// <item>RFC 7830; RFC 8467, 4.1: queries SHOULD be padded to a multiple of 128 bytes.</item>
/// <item>One <c>HttpClient</c> is reused for all queries, so connections are pooled; a query timeout is applied.</item>
/// </list>
/// </remarks>
public sealed class DohRequestResolver(IPEndPoint dnsEndpoint, string host, string path, ILogger logger) : IRequestResolver
{
    public Task<IResponse> Resolve(IRequest request, RequestTrace trace, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException($"DNS over HTTPS client for https://{host}{path} ({dnsEndpoint}) is not implemented yet");
    }
}
