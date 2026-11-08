using System;
using System.Buffers.Text;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Charon.Dns.Lib.Protocol;
using Charon.Dns.Lib.Protocol.ResourceRecords;
using Charon.Dns.Lib.Protocol.Utils;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;
using ILogger = Serilog.ILogger;

namespace Charon.Dns.Lib.Server.Transports;

/// <summary>
/// DNS over HTTPS (RFC 8484).
/// </summary>
internal sealed class HttpsDnsListener(DnsServer server, X509Certificate2 certificate, string path, ILogger logger)
{
    private const string DnsMessageContentType = "application/dns-message";
    private const string DnsQueryParameter = "dns";

    public async Task Listen(IPEndPoint endpoint, bool enableIpV6, CancellationToken cancellationToken)
    {
        await using var app = CreateApplication(endpoint, enableIpV6);
        await app.StartAsync(cancellationToken);

        try
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Stopped together with the other listeners
        }
        finally
        {
            await app.StopAsync(CancellationToken.None);
        }
    }

    private WebApplication CreateApplication(IPEndPoint endpoint, bool enableIpV6)
    {
        var builder = WebApplication.CreateSlimBuilder();
        // Requests are logged by the DNS server, the host must not log or handle process signals on its own
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton<IHostLifetime, NoopHostLifetime>();
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.AddServerHeader = false;
            kestrel.Limits.MaxRequestBodySize = ResponseSerializer.MaxMessageSize;

            var address = enableIpV6 && endpoint.Address.Equals(IPAddress.Any) ? IPAddress.IPv6Any : endpoint.Address;
            kestrel.Listen(address, endpoint.Port, listen =>
            {
                // RFC 8484, 5.2: HTTP/2 is the recommended minimum, HTTP/1.1 is kept for simple clients
                listen.Protocols = HttpProtocols.Http1AndHttp2;
                listen.UseHttps(certificate);
            });
        });

        var app = builder.Build();
        app.Run(HandleRequest);
        return app;
    }

    private async Task HandleRequest(HttpContext context)
    {
        var httpRequest = context.Request;
        var httpResponse = context.Response;
        if (!string.Equals(httpRequest.Path.Value, path, StringComparison.Ordinal))
        {
            httpResponse.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var cancellationToken = context.RequestAborted;
        byte[]? message;
        try
        {
            message = await ReadMessage(httpRequest, httpResponse, cancellationToken);
        }
        catch (Microsoft.AspNetCore.Http.BadHttpRequestException e)
        {
            // The body is larger than the maximum message size
            httpResponse.StatusCode = e.StatusCode;
            return;
        }

        if (message is null)
        {
            return;
        }

        if (message.Length < Header.HeaderSize)
        {
            httpResponse.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var connection = context.Connection;
        var remoteAddress = connection.RemoteIpAddress ?? IPAddress.None;
        var remoteEndPoint = new IPEndPoint(remoteAddress, connection.RemotePort);
        var remote = remoteEndPoint.Normalize();

        var exchange = await server.ProcessMessage(message, remote, cancellationToken);
        if (exchange is null)
        {
            // A response was sent as a request
            httpResponse.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        await WriteResponse(httpResponse, exchange, message, cancellationToken);
    }

    /// <summary>
    /// RFC 8484, 4.1: a GET request carries the message in the "dns" parameter, a POST request in the body.
    /// </summary>
    /// <returns>Null if the request is invalid, the status code is set then.</returns>
    private static async Task<byte[]?> ReadMessage(
        HttpRequest httpRequest,
        HttpResponse httpResponse,
        CancellationToken cancellationToken)
    {
        if (HttpMethods.IsGet(httpRequest.Method))
        {
            var query = httpRequest.Query[DnsQueryParameter];
            if (query.Count != 1 || !TryDecodeBase64Url(query[0], out var message))
            {
                httpResponse.StatusCode = StatusCodes.Status400BadRequest;
                return null;
            }

            return message;
        }

        if (HttpMethods.IsPost(httpRequest.Method))
        {
            if (!IsDnsMessage(httpRequest.ContentType))
            {
                httpResponse.StatusCode = StatusCodes.Status415UnsupportedMediaType;
                return null;
            }

            using var body = new MemoryStream();
            await httpRequest.Body.CopyToAsync(body, cancellationToken);
            return body.ToArray();
        }

        httpResponse.StatusCode = StatusCodes.Status405MethodNotAllowed;
        httpResponse.Headers.Allow = "GET, POST";
        return null;
    }

    /// <summary>
    /// RFC 8484, 6: base64url without padding (RFC 4648, 5).
    /// </summary>
    private static bool TryDecodeBase64Url(string? value, out byte[] message)
    {
        message = [];
        if (string.IsNullOrEmpty(value) || value.Length > Base64Url.GetEncodedLength(ResponseSerializer.MaxMessageSize))
        {
            return false;
        }

        try
        {
            message = Base64Url.DecodeFromChars(value);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool IsDnsMessage(string? contentType)
    {
        return MediaTypeHeaderValue.TryParse(contentType, out var mediaType)
            && mediaType.MediaType.Equals(DnsMessageContentType, StringComparison.OrdinalIgnoreCase);
    }

    private async Task WriteResponse(
        HttpResponse httpResponse,
        DnsExchange exchange,
        byte[] message,
        CancellationToken cancellationToken)
    {
        var (request, response, trace) = exchange;
        var pad = EdnsPadding.IsRequested(request);
        byte[] buffer;
        int size;

        try
        {
            buffer = ResponseSerializer.Rent(response, pad, prefixSize: 0, out size);
        }
        catch (Exception e)
        {
            trace.Logger.Error(e, "Dns server error. Unable to serialize response");
            await server.OnError(e, trace);

            var errorResponse = server.CreateErrorResponse(request, message);
            if (errorResponse is null)
            {
                httpResponse.StatusCode = StatusCodes.Status500InternalServerError;
                return;
            }

            response = errorResponse;
            buffer = ResponseSerializer.Rent(response, pad, prefixSize: 0, out size);
        }

        try
        {
            // RFC 8484, 4.2.1: DNS errors are returned in the message with a successful HTTP status
            httpResponse.StatusCode = StatusCodes.Status200OK;
            httpResponse.ContentType = DnsMessageContentType;
            httpResponse.ContentLength = size;
            httpResponse.Headers.CacheControl = GetCacheControl(response);

            await httpResponse.Body.WriteAsync(buffer.AsMemory(0, size), cancellationToken);

            trace.Logger.Debug("Dns server: HTTPS response sent to {Remote}", trace.RemoteEndPoint);
        }
        catch (Exception e) when (e is OperationCanceledException or IOException)
        {
            // The client is gone
            logger.Debug("Dns server: unable to send HTTPS response to {Remote}: {Reason}", trace.RemoteEndPoint, e.Message);
        }
        finally
        {
            ResponseSerializer.Return(buffer);
        }
    }

    /// <summary>
    /// RFC 8484, 5.1: the freshness lifetime must not exceed the smallest TTL of the answer.
    /// A negative answer is cached for the SOA minimum (RFC 2308, 5).
    /// </summary>
    internal static string GetCacheControl(IReadOnlyResponse response)
    {
        TimeSpan? maxAge = null;
        if (response.AnswerRecords.Count > 0)
        {
            maxAge = response.AnswerRecords.Min(x => x.TimeToLive);
        }
        else if (response.ResponseCode is ResponseCode.NoError or ResponseCode.NameError)
        {
            var soa = response.AuthorityRecords
                .OfType<StartOfAuthorityResourceRecord>()
                .FirstOrDefault();
            if (soa is not null)
            {
                maxAge = soa.TimeToLive < soa.MinimumTimeToLive ? soa.TimeToLive : soa.MinimumTimeToLive;
            }
        }

        if (maxAge is not { } value)
        {
            return "no-store";
        }

        var seconds = (long)Math.Max(0, value.TotalSeconds);
        return $"max-age={seconds.ToString(CultureInfo.InvariantCulture)}";
    }

    private sealed class NoopHostLifetime : IHostLifetime
    {
        public Task WaitForStartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
