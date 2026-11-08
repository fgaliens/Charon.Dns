#nullable enable
using System;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Charon.Dns.Lib.Client.RequestResolver;
using Charon.Dns.Lib.Protocol;
using Charon.Dns.Lib.Protocol.ResourceRecords;
using Charon.Dns.Lib.Server;
using Charon.Dns.Lib.Tracing;
using Charon.Dns.Utils;
using Charon.Dns.Utils.Units;
using Serilog.Core;

namespace Charon.Dns.Lib.Tests.Server.Transports;

internal sealed class TransportTestServer : IAsyncDisposable
{
    public static readonly Domain Name = new("my.home");
    public static readonly Domain SlowName = new("slow.my.home");
    public static readonly Domain FailingName = new("fail.my.home");

    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(10);

    private readonly CancellationTokenSource _cancellation = new();
    private readonly Task _listening;

    private TransportTestServer(Func<DnsServer, IPEndPoint, CancellationToken, Task> listen)
    {
        var port = GetFreePort();
        EndPoint = new IPEndPoint(IPAddress.Loopback, port);
        var requestCounter = new RequestCounter();
        var socketBufferSize = new ByteUnit(64 * 1024);
        var server = new DnsServer(new TestResolver(), requestCounter, 1, socketBufferSize, Logger.None,
            recursionAvailable: true);
        _listening = listen(server, EndPoint, _cancellation.Token);
    }

    public IPEndPoint EndPoint { get; }

    public static Task<TransportTestServer> StartTcp()
    {
        return Start((server, endPoint, token) => server.ListenTcp(endPoint, enableIpV6: false, token));
    }

    public static Task<TransportTestServer> StartTls(X509Certificate2 certificate)
    {
        return Start((server, endPoint, token) => server.ListenTls(endPoint, enableIpV6: false, certificate, token));
    }

    public static Task<TransportTestServer> StartHttps(X509Certificate2 certificate)
    {
        return Start((server, endPoint, token) =>
            server.ListenHttps(endPoint, enableIpV6: false, certificate, "/dns-query", token));
    }

    public static X509Certificate2 CreateCertificate()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var notBefore = DateTimeOffset.UtcNow.AddDays(-1);
        var notAfter = DateTimeOffset.UtcNow.AddDays(1);
        using var certificate = request.CreateSelfSigned(notBefore, notAfter);
        // A certificate with an ephemeral key can't be used by a TLS server on every platform
        var pkcs12 = certificate.Export(X509ContentType.Pkcs12);
        return X509CertificateLoader.LoadPkcs12(pkcs12, null);
    }

    public static Request CreateRequest(Domain name, int id = 4242)
    {
        var request = new Request
        {
            Id = id,
            RecursionDesired = true,
        };
        var question = new Question(name, RecordType.TXT);
        request.Questions.Add(question);
        return request;
    }

    public async ValueTask DisposeAsync()
    {
        await _cancellation.CancelAsync();
        try
        {
            await _listening;
        }
        catch (OperationCanceledException)
        {
        }

        _cancellation.Dispose();
    }

    private static async Task<TransportTestServer> Start(Func<DnsServer, IPEndPoint, CancellationToken, Task> listen)
    {
        var server = new TransportTestServer(listen);
        using var timeout = new CancellationTokenSource(StartTimeout);
        while (true)
        {
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(server.EndPoint, timeout.Token);
                return server;
            }
            catch (SocketException)
            {
                await Task.Delay(50, timeout.Token);
            }
        }
    }

    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>
    /// Answers with 10 TXT records of 200 bytes, so the response doesn't fit into 512 bytes.
    /// </summary>
    private sealed class TestResolver : IRequestResolver
    {
        public const int RecordCount = 10;

        public async Task<IResponse> Resolve(IRequest request, RequestTrace trace, CancellationToken cancellationToken = default)
        {
            var name = request.Questions[0].Name;
            if (name.Equals(FailingName))
            {
                throw new InvalidOperationException("Upstream is not available");
            }

            if (name.Equals(SlowName))
            {
                await Task.Delay(500, cancellationToken);
            }

            var response = Response.FromRequest(request);
            var text = new string('x', 200);
            var characterStrings = CharacterString.FromString(text);
            for (var i = 0; i < RecordCount; i++)
            {
                var ttl = TimeSpan.FromSeconds(60 + i);
                var record = new TextResourceRecord(name, characterStrings, ttl);
                response.AnswerRecords.Add(record);
            }

            foreach (var additionalRecord in request.AdditionalRecords)
            {
                response.AdditionalRecords.Add(additionalRecord);
            }

            return response;
        }
    }
}
