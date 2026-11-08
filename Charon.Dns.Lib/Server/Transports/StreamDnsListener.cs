using System;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace Charon.Dns.Lib.Server.Transports;

/// <summary>
/// Accepts DNS over TCP (RFC 7766) connections, or DNS over TLS (RFC 7858) ones when a certificate is set.
/// </summary>
internal sealed class StreamDnsListener(DnsServer server, X509Certificate2? certificate, ILogger logger)
{
    private const int Backlog = 512;
    // RFC 7766, 10: the number of connections is limited, so they can't exhaust the server
    private const int MaxConnections = 512;
    private static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(5);
    // ALPN identifier of DNS over TLS, registered by IANA
    private static readonly SslApplicationProtocol DotProtocol = new("dot");

    private readonly SemaphoreSlim _connections = new(MaxConnections, MaxConnections);

    private string Transport => certificate is null ? "TCP" : "TLS";

    public async Task Listen(IPEndPoint endpoint, bool enableIpV6, CancellationToken cancellationToken)
    {
        using var socket = CreateSocket(endpoint, enableIpV6);

        while (!cancellationToken.IsCancellationRequested)
        {
            Socket client;
            try
            {
                client = await socket.AcceptAsync(cancellationToken);
            }
            catch (SocketException e)
            {
                // A client may drop the connection before it is accepted
                logger.Debug(e, "Dns server: unable to accept {Transport} connection", Transport);
                continue;
            }

            if (!_connections.Wait(0))
            {
                logger.Warning("Dns server: too many {Transport} connections, connection from {Remote} rejected",
                    Transport, client.RemoteEndPoint);
                client.Dispose();
                continue;
            }

            _ = Task.Run(() => HandleConnection(client, cancellationToken), CancellationToken.None);
        }
    }

    private async Task HandleConnection(Socket client, CancellationToken cancellationToken)
    {
        var remoteEndPoint = (IPEndPoint)client.RemoteEndPoint!;
        var remote = remoteEndPoint.Normalize();
        Stream stream = new NetworkStream(client, ownsSocket: true);

        try
        {
            client.NoDelay = true;

            if (certificate is not null)
            {
                var sslStream = new SslStream(stream, leaveInnerStreamOpen: false);
                stream = sslStream;
                await Authenticate(sslStream, cancellationToken);
            }

            logger.Debug("Dns server: {Transport} connection from {Remote} accepted", Transport, remote);

            var connection = new StreamDnsConnection(
                server,
                stream,
                remote,
                padResponses: certificate is not null,
                logger,
                cancellationToken);
            await connection.Run();
        }
        catch (Exception e) when (e is IOException or AuthenticationException or OperationCanceledException)
        {
            logger.Debug("Dns server: {Transport} connection from {Remote} failed: {Reason}", Transport, remote, e.Message);
        }
        catch (Exception e)
        {
            logger.Warning(e, "Dns server: {Transport} connection from {Remote} failed", Transport, remote);
        }
        finally
        {
            await stream.DisposeAsync();
            _connections.Release();
        }
    }

    private async Task Authenticate(SslStream sslStream, CancellationToken cancellationToken)
    {
        using var handshakeCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        handshakeCancellation.CancelAfter(HandshakeTimeout);

        var options = new SslServerAuthenticationOptions
        {
            ServerCertificate = certificate,
            // BCP 195 (RFC 9325): TLS 1.2 is the minimum
            EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            ApplicationProtocols = [DotProtocol],
            ClientCertificateRequired = false,
        };
        await sslStream.AuthenticateAsServerAsync(options, handshakeCancellation.Token);
    }

    private static Socket CreateSocket(IPEndPoint endpoint, bool enableIpV6)
    {
        var addressFamily = enableIpV6 ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork;
        var socket = new Socket(addressFamily, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            socket.Bind(endpoint);
            socket.Listen(Backlog);
        }
        catch
        {
            socket.Dispose();
            throw;
        }

        return socket;
    }
}
