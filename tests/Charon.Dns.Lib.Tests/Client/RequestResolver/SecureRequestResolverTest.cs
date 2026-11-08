#nullable enable
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Charon.Dns.Lib.Client;
using Charon.Dns.Lib.Client.RequestResolver;
using Charon.Dns.Lib.Protocol;
using Charon.Dns.Lib.Protocol.ResourceRecords;
using Charon.Dns.Lib.Tests.Server.Transports;
using Charon.Dns.Lib.Tracing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Logging;
using Serilog.Core;
using Xunit;

namespace Charon.Dns.Lib.Tests.Client.RequestResolver;

/// <summary>
/// Resolvers against the transports of <see cref="Lib.Server.DnsServer"/>.
/// </summary>
public class SecureRequestResolverTest
{
    private const string Host = "localhost";
    private const string DohPath = "/dns-query";
    private const ushort PaddingOptionCode = 12;
    private static readonly Domain RootName = new("");

    [Fact]
    public async Task UdpAndTcp_TruncatedResponse_IsRetriedOverTcp()
    {
        await using var server = await TransportTestServer.StartUdpAndTcp();
        using var resolver = new UdpAndTcpRequestResolver(server.EndPoint, null, Logger.None);
        // The answer doesn't fit into 512 bytes of a request without EDNS
        var request = TransportTestServer.CreateRequest(TransportTestServer.Name);

        var response = await resolver.Resolve(request, RequestTrace.Empty);

        Assert.Equal(request.Id, response.Id);
        Assert.False(response.Truncated);
        Assert.Equal(10, response.AnswerRecords.Count);
    }

    [Fact]
    public async Task Dot_ResolvesQuery()
    {
        using var certificate = TransportTestServer.CreateCertificate();
        await using var server = await TransportTestServer.StartTls(certificate);
        using var resolver = new DotRequestResolver(server.EndPoint, Host, Logger.None, (_, _, _, _) => true);
        var request = TransportTestServer.CreateRequest(TransportTestServer.Name);

        var response = await resolver.Resolve(request, RequestTrace.Empty);

        Assert.Equal(request.Id, response.Id);
        Assert.Equal(10, response.AnswerRecords.Count);
    }

    [Fact]
    public async Task Dot_PadsEdnsQuery()
    {
        using var certificate = TransportTestServer.CreateCertificate();
        await using var server = await TransportTestServer.StartTls(certificate);
        using var resolver = new DotRequestResolver(server.EndPoint, Host, Logger.None, (_, _, _, _) => true);
        var request = CreateEdnsRequest();

        var response = await resolver.Resolve(request, RequestTrace.Empty);

        // The server pads the response only to a padded query (RFC 7830, 4)
        Assert.True(HasPadding(response));
        Assert.Equal(0, response.Size % 468);
        // The original request is not changed
        var requestOpt = Assert.Single(request.AdditionalRecords);
        Assert.Empty(requestOpt.Data);
    }

    [Fact]
    public async Task Dot_UntrustedCertificate_Fails()
    {
        using var certificate = TransportTestServer.CreateCertificate();
        await using var server = await TransportTestServer.StartTls(certificate);
        using var resolver = new DotRequestResolver(server.EndPoint, Host, Logger.None);
        var request = TransportTestServer.CreateRequest(TransportTestServer.Name);

        var resolving = resolver.Resolve(request, RequestTrace.Empty);

        // RFC 8310, 5: no fallback to an unauthenticated connection
        await Assert.ThrowsAsync<AuthenticationException>(() => resolving);
    }

    [Fact]
    public async Task Doh_ResolvesQueryAndRestoresId()
    {
        using var certificate = TransportTestServer.CreateCertificate();
        await using var server = await TransportTestServer.StartHttps(certificate);
        using var resolver = new DohRequestResolver(server.EndPoint, Host, DohPath, Logger.None, (_, _, _, _) => true);
        var request = CreateEdnsRequest();

        var response = await resolver.Resolve(request, RequestTrace.Empty);

        // RFC 8484, 4.1: the query is sent with id 0
        Assert.Equal(request.Id, response.Id);
        Assert.Equal(10, response.AnswerRecords.Count);
        Assert.True(HasPadding(response));
    }

    [Fact]
    public async Task Doh_ServerFailure_IsReturned()
    {
        using var certificate = TransportTestServer.CreateCertificate();
        await using var server = await TransportTestServer.StartHttps(certificate);
        using var resolver = new DohRequestResolver(server.EndPoint, Host, DohPath, Logger.None, (_, _, _, _) => true);
        var request = TransportTestServer.CreateRequest(TransportTestServer.FailingName);

        var response = await resolver.Resolve(request, RequestTrace.Empty);

        Assert.Equal(ResponseCode.ServerFailure, response.ResponseCode);
    }

    [Fact]
    public async Task Doh_HttpError_Fails()
    {
        using var certificate = TransportTestServer.CreateCertificate();
        await using var server = await TransportTestServer.StartHttps(certificate);
        using var resolver = new DohRequestResolver(server.EndPoint, Host, "/other", Logger.None, (_, _, _, _) => true);
        var request = TransportTestServer.CreateRequest(TransportTestServer.Name);

        var resolving = resolver.Resolve(request, RequestTrace.Empty);

        await Assert.ThrowsAsync<ResponseException>(() => resolving);
    }

    [Fact]
    public async Task Doh_UsesHttp2()
    {
        // Some servers (Quad9) answer HTTP/1.1 with 505 HTTP Version Not Supported
        using var certificate = TransportTestServer.CreateCertificate();
        await using var server = await StartHttp2OnlyServer(certificate);
        var endPoint = GetEndPoint(server);
        using var resolver = new DohRequestResolver(endPoint, Host, DohPath, Logger.None, (_, _, _, _) => true);
        var request = TransportTestServer.CreateRequest(TransportTestServer.Name);

        var response = await resolver.Resolve(request, RequestTrace.Empty);

        Assert.Equal(request.Id, response.Id);
        Assert.Equal(ResponseCode.NoError, response.ResponseCode);
    }

    private static async Task<WebApplication> StartHttp2OnlyServer(X509Certificate2 certificate)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0, listen =>
        {
            listen.Protocols = HttpProtocols.Http2;
            listen.UseHttps(certificate);
        }));

        var app = builder.Build();
        app.Run(async context =>
        {
            if (!HttpProtocol.IsHttp2(context.Request.Protocol))
            {
                context.Response.StatusCode = StatusCodes.Status505HttpVersionNotsupported;
                return;
            }

            using var body = new MemoryStream();
            await context.Request.Body.CopyToAsync(body);
            var request = Request.FromArray(body.ToArray());
            var response = Response.FromRequest(request);
            context.Response.ContentType = "application/dns-message";
            await context.Response.Body.WriteAsync(response.ToArray());
        });

        await app.StartAsync();
        return app;
    }

    private static IPEndPoint GetEndPoint(WebApplication app)
    {
        var address = app.Urls.Single();
        var uri = new System.Uri(address);
        return new IPEndPoint(IPAddress.Loopback, uri.Port);
    }

    private static Request CreateEdnsRequest()
    {
        var request = TransportTestServer.CreateRequest(TransportTestServer.Name);
        var opt = new ResourceRecord(RootName, [], RecordType.OPT, (RecordClass)1232);
        request.AdditionalRecords.Add(opt);
        return request;
    }

    private static bool HasPadding(IResponse response)
    {
        var opt = Assert.Single(response.AdditionalRecords);
        return opt.Data.Length >= 4 && BinaryPrimitives.ReadUInt16BigEndian(opt.Data) == PaddingOptionCode;
    }
}
