using System;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Charon.Dns.Lib.Protocol;
using Charon.Dns.Lib.Protocol.Utils;
using Serilog;

namespace Charon.Dns.Lib.Client.RequestResolver;

/// <summary>
/// DNS over TLS client (RFC 7858) with the Strict privacy profile (RFC 8310).
/// </summary>
/// <remarks>
/// Messages are framed and pipelined as over TCP (RFC 7858, 3.3), see <see cref="StreamRequestResolver"/>.
/// The server certificate is validated against <c>host</c>; a failed validation fails the query,
/// there is no fallback to plain DNS (RFC 8310, 5 and 8).
/// </remarks>
public sealed class DotRequestResolver : StreamRequestResolver
{
    // The first query also opens the connection and does the TLS handshake
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(5);

    private readonly SslClientAuthenticationOptions _authenticationOptions;

    public DotRequestResolver(IPEndPoint dnsEndpoint, string host, ILogger logger)
        : this(dnsEndpoint, host, logger, certificateValidation: null)
    {
    }

    /// <param name="certificateValidation">Replaces the certificate validation, for tests only.</param>
    internal DotRequestResolver(
        IPEndPoint dnsEndpoint,
        string host,
        ILogger logger,
        RemoteCertificateValidationCallback? certificateValidation)
        : base(dnsEndpoint, QueryTimeout, logger)
    {
        _authenticationOptions = new SslClientAuthenticationOptions
        {
            // The certificate is validated against the host name or the IP address.
            // SNI is sent only for a host name. TLS sessions are resumed on reconnect by the platform
            TargetHost = host,
            // BCP 195 (RFC 9325): TLS 1.2 is the minimum
            EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            // Revocation lists would be downloaded by names resolved through this server
            CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
            RemoteCertificateValidationCallback = certificateValidation,
        };

        logger.Information("TLS request resolver for DNS {DNS} ({Host}) created", dnsEndpoint, host);
    }

    protected override string Transport => "TLS";

    protected override async Task<Stream> Connect(CancellationToken cancellationToken)
    {
        var socket = await ConnectSocket(cancellationToken);
        var networkStream = new NetworkStream(socket, ownsSocket: true);
        var sslStream = new SslStream(networkStream, leaveInnerStreamOpen: false);

        try
        {
            await sslStream.AuthenticateAsClientAsync(_authenticationOptions, cancellationToken);
        }
        catch
        {
            await sslStream.DisposeAsync();
            throw;
        }

        return sslStream;
    }

    /// <summary>
    /// RFC 8467, 4.1: queries are padded, so their size doesn't reveal the name.
    /// </summary>
    protected override IRequest PrepareRequest(IRequest request)
    {
        return EdnsPadding.PadRequest(request);
    }
}
