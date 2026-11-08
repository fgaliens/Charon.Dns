using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Charon.Dns.Lib.Extensions;
using Charon.Dns.Lib.Protocol;
using Charon.Dns.Lib.Protocol.EqualityComparers;
using Charon.Dns.Lib.Protocol.Utils;
using Charon.Dns.Lib.Tracing;
using Serilog;

namespace Charon.Dns.Lib.Client.RequestResolver;

/// <summary>
/// DNS over HTTPS client (RFC 8484).
/// </summary>
/// <remarks>
/// Queries are sent with POST (RFC 8484, 4.1) over HTTP/2 (RFC 8484, 5.2), connections are pooled by <see cref="HttpClient"/>.
/// Connections go to the configured address instead of the one the system resolves for <c>host</c>,
/// as the system resolver may be this server itself.
/// </remarks>
public sealed class DohRequestResolver : IRequestResolver, IDisposable
{
    private const string DnsMessageContentType = "application/dns-message";
    private const int MaxMessageSize = ushort.MaxValue;
    // The first query also opens the connection and does the TLS handshake
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1);
    private static readonly MediaTypeWithQualityHeaderValue AcceptDnsMessage = new(DnsMessageContentType);

    private readonly IPEndPoint _dnsEndpoint;
    private readonly Uri _uri;
    private readonly HttpClient _httpClient;
    private readonly CancellationTokenSource _resolvingCancellation = new();

    public DohRequestResolver(IPEndPoint dnsEndpoint, string host, string path, ILogger logger)
        : this(dnsEndpoint, host, path, logger, certificateValidation: null)
    {
    }

    /// <param name="dnsEndpoint">Address and port to connect to.</param>
    /// <param name="host">Host of the request URI, the server certificate is validated against it.</param>
    /// <param name="path">Path of the request URI.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="certificateValidation">Replaces the certificate validation, for tests only.</param>
    internal DohRequestResolver(
        IPEndPoint dnsEndpoint,
        string host,
        string path,
        ILogger logger,
        RemoteCertificateValidationCallback? certificateValidation)
    {
        _dnsEndpoint = dnsEndpoint;
        // RFC 8484, 3: the URI template without variables, the message goes in the body
        var uriBuilder = new UriBuilder(Uri.UriSchemeHttps, host, dnsEndpoint.Port, path);
        _uri = uriBuilder.Uri;

        var handler = new SocketsHttpHandler
        {
            ConnectCallback = Connect,
            UseProxy = false,
            UseCookies = false,
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
            PooledConnectionIdleTimeout = PooledConnectionIdleTimeout,
            EnableMultipleHttp2Connections = true,
            SslOptions = new SslClientAuthenticationOptions
            {
                // The target host is the host of the URI: the certificate is validated against it
                // BCP 195 (RFC 9325): TLS 1.2 is the minimum
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                // Revocation lists would be downloaded by names resolved through this server
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
                RemoteCertificateValidationCallback = certificateValidation,
            },
        };
        _httpClient = new HttpClient(handler)
        {
            MaxResponseContentBufferSize = MaxMessageSize,
            // The query timeout is applied by the cancellation token
            Timeout = Timeout.InfiniteTimeSpan,
        };

        logger.Information("HTTPS request resolver for DNS {DNS} ({Uri}) created", dnsEndpoint, _uri);
    }

    public async Task<IResponse> Resolve(IRequest request, RequestTrace trace, CancellationToken cancellationToken = default)
    {
        var logger = trace.Logger;

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            _resolvingCancellation.Token,
            cancellationToken);
        linkedCancellation.CancelAfter(QueryTimeout);
        var linkedToken = linkedCancellation.Token;

        try
        {
            var message = CreateMessage(request);
            using var content = new ByteArrayContent(message);
            content.Headers.ContentType = new MediaTypeHeaderValue(DnsMessageContentType);
            // The version of a created message is not taken from HttpClient defaults, it is 1.1 unless set.
            // Some servers (Quad9) answer HTTP/1.1 with 505
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _uri);
            httpRequest.Content = content;
            httpRequest.Version = HttpVersion.Version20;
            httpRequest.VersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
            httpRequest.Headers.Accept.Add(AcceptDnsMessage);

            using var httpResponse = await _httpClient.SendAsync(httpRequest, linkedToken);
            var responseMessage = await ReadMessage(httpResponse, linkedToken);
            var response = ParseResponse(request, responseMessage);

            logger.Debug("Request resolving completed over HTTP/{Version}. Returning result ({Size} bytes)",
                httpResponse.Version, responseMessage.Length);
            return response;
        }
        catch (Exception e)
        {
            logger.Error(e, "Request resolving error! HTTPS DNS {Uri}, request id: {Id}", _uri, request.Id);
            throw;
        }
    }

    public void Dispose()
    {
        if (_resolvingCancellation.IsCancellationRequested)
        {
            return;
        }

        _resolvingCancellation.Cancel();
        _httpClient.Dispose();
    }

    private async ValueTask<Stream> Connect(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var socket = new Socket(_dnsEndpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            socket.NoDelay = true;
            await socket.ConnectAsync(_dnsEndpoint, cancellationToken);
        }
        catch
        {
            socket.Dispose();
            throw;
        }

        return new NetworkStream(socket, ownsSocket: true);
    }

    /// <summary>
    /// RFC 8484, 4.1: the id is 0, so equal queries get equal HTTP requests that HTTP caches can share.
    /// RFC 8467, 4.1: the query is padded, so its size doesn't reveal the name.
    /// </summary>
    private static byte[] CreateMessage(IRequest request)
    {
        var paddedRequest = EdnsPadding.PadRequest(request);
        var message = paddedRequest.ToArray();
        var messageHeader = message.DnsMessage.Header;
        messageHeader.Id = 0;
        return message;
    }

    /// <summary>
    /// RFC 8484, 4.2.1: a DNS error comes with a successful status, any other status is an HTTP failure.
    /// </summary>
    private static async Task<byte[]> ReadMessage(HttpResponseMessage httpResponse, CancellationToken cancellationToken)
    {
        if (!httpResponse.IsSuccessStatusCode)
        {
            throw new ResponseException($"HTTP status {(int)httpResponse.StatusCode} {httpResponse.ReasonPhrase}");
        }

        var contentType = httpResponse.Content.Headers.ContentType?.MediaType;
        if (!string.Equals(contentType, DnsMessageContentType, StringComparison.OrdinalIgnoreCase))
        {
            throw new ResponseException($"Unexpected content type '{contentType}'");
        }

        // The content is buffered up to MaxResponseContentBufferSize
        var message = await httpResponse.Content.ReadAsByteArrayAsync(cancellationToken);
        if (message.Length < Header.HeaderSize)
        {
            throw new ResponseException($"Response of {message.Length} bytes is too short");
        }

        return message;
    }

    private static Response ParseResponse(IRequest request, byte[] message)
    {
        var messageHeader = message.DnsMessage.Header;
        if (messageHeader.Id != 0)
        {
            throw new ResponseException($"Response id {messageHeader.Id} doesn't match request id 0");
        }

        messageHeader.Id = (ushort)request.Id;
        var response = Response.FromArray(message);
        if (!request.Questions.SequenceEqual(response.Questions, QuestionComparer.Instance))
        {
            throw new ResponseException(response, "Question in response doesn't match request");
        }

        return response;
    }
}
