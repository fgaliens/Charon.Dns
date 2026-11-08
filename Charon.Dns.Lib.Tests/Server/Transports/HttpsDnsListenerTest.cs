#nullable enable
using System;
using System.Buffers.Text;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Charon.Dns.Lib.Protocol;
using Charon.Dns.Lib.Protocol.ResourceRecords;
using Charon.Dns.Lib.Server.Transports;
using JetBrains.Annotations;
using Xunit;

namespace Charon.Dns.Lib.Tests.Server.Transports;

[TestSubject(typeof(HttpsDnsListener))]
public class HttpsDnsListenerTest
{
    private const string DnsMessageContentType = "application/dns-message";

    [Fact]
    public async Task Post_IsAnsweredOverHttp2()
    {
        using var certificate = TransportTestServer.CreateCertificate();
        await using var server = await TransportTestServer.StartHttps(certificate);
        using var client = CreateClient();
        var request = TransportTestServer.CreateRequest(TransportTestServer.Name, id: 0);
        using var content = new ByteArrayContent(request.ToArray());
        content.Headers.ContentType = new MediaTypeHeaderValue(DnsMessageContentType);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, GetUri(server))
        {
            Content = content,
            Version = HttpVersion.Version20,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact,
        };

        using var httpResponse = await client.SendAsync(httpRequest);

        Assert.Equal(HttpStatusCode.OK, httpResponse.StatusCode);
        Assert.Equal(HttpVersion.Version20, httpResponse.Version);
        Assert.Equal(DnsMessageContentType, httpResponse.Content.Headers.ContentType?.MediaType);
        // RFC 8484, 5.1: the smallest TTL of the answer
        Assert.Equal(TimeSpan.FromSeconds(60), httpResponse.Headers.CacheControl?.MaxAge);
        var body = await httpResponse.Content.ReadAsByteArrayAsync();
        var response = Response.FromArray(body);
        Assert.Equal(0, response.Id);
        Assert.Equal(10, response.AnswerRecords.Count);
    }

    [Fact]
    public async Task Get_DecodesBase64UrlParameter()
    {
        using var certificate = TransportTestServer.CreateCertificate();
        await using var server = await TransportTestServer.StartHttps(certificate);
        using var client = CreateClient();
        var request = TransportTestServer.CreateRequest(TransportTestServer.Name, id: 0);
        var message = request.ToArray();
        var dns = Base64Url.EncodeToString(message);

        using var httpResponse = await client.GetAsync($"{GetUri(server)}?dns={dns}");

        Assert.Equal(HttpStatusCode.OK, httpResponse.StatusCode);
        var body = await httpResponse.Content.ReadAsByteArrayAsync();
        var response = Response.FromArray(body);
        Assert.Equal(request.Questions.Single().Name, response.Questions.Single().Name);
    }

    [Fact]
    public async Task ResolverError_IsAnsweredWithSuccessfulStatus()
    {
        using var certificate = TransportTestServer.CreateCertificate();
        await using var server = await TransportTestServer.StartHttps(certificate);
        using var client = CreateClient();
        var request = TransportTestServer.CreateRequest(TransportTestServer.FailingName, id: 0);
        using var content = new ByteArrayContent(request.ToArray());
        content.Headers.ContentType = new MediaTypeHeaderValue(DnsMessageContentType);

        using var httpResponse = await client.PostAsync(GetUri(server), content);

        Assert.Equal(HttpStatusCode.OK, httpResponse.StatusCode);
        Assert.True(httpResponse.Headers.CacheControl?.NoStore);
        var body = await httpResponse.Content.ReadAsByteArrayAsync();
        var response = Response.FromArray(body);
        Assert.Equal(ResponseCode.ServerFailure, response.ResponseCode);
    }

    [Theory]
    [InlineData("?dns=%%%")]
    [InlineData("?dns=AAAA")]
    [InlineData("")]
    public async Task Get_InvalidParameter_IsBadRequest(string query)
    {
        using var certificate = TransportTestServer.CreateCertificate();
        await using var server = await TransportTestServer.StartHttps(certificate);
        using var client = CreateClient();

        using var httpResponse = await client.GetAsync($"{GetUri(server)}{query}");

        Assert.Equal(HttpStatusCode.BadRequest, httpResponse.StatusCode);
    }

    [Fact]
    public async Task Post_OtherContentType_IsUnsupported()
    {
        using var certificate = TransportTestServer.CreateCertificate();
        await using var server = await TransportTestServer.StartHttps(certificate);
        using var client = CreateClient();
        var request = TransportTestServer.CreateRequest(TransportTestServer.Name, id: 0);
        using var content = new ByteArrayContent(request.ToArray());
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        using var httpResponse = await client.PostAsync(GetUri(server), content);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, httpResponse.StatusCode);
    }

    [Fact]
    public async Task OtherMethodAndPath_AreRejected()
    {
        using var certificate = TransportTestServer.CreateCertificate();
        await using var server = await TransportTestServer.StartHttps(certificate);
        using var client = CreateClient();
        using var content = new ByteArrayContent([]);

        using var putResponse = await client.PutAsync(GetUri(server), content);
        using var otherPathResponse = await client.GetAsync($"https://localhost:{server.EndPoint.Port}/other");

        Assert.Equal(HttpStatusCode.MethodNotAllowed, putResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, otherPathResponse.StatusCode);
    }

    [Fact]
    public void GetCacheControl_UsesSoaMinimumForNegativeResponse()
    {
        var zone = new Domain("my.home");
        var master = new Domain("ns.my.home");
        var responsible = new Domain("admin.my.home");
        var soa = new StartOfAuthorityResourceRecord(zone, master, responsible, 1,
            TimeSpan.FromHours(1), TimeSpan.FromMinutes(10), TimeSpan.FromDays(1), TimeSpan.FromSeconds(30),
            TimeSpan.FromMinutes(5));
        var response = new Response
        {
            ResponseCode = ResponseCode.NameError,
        };
        response.AuthorityRecords.Add(soa);

        var cacheControl = HttpsDnsListener.GetCacheControl(response);

        Assert.Equal("max-age=30", cacheControl);
    }

    private static string GetUri(TransportTestServer server)
    {
        return $"https://localhost:{server.EndPoint.Port}/dns-query";
    }

    private static HttpClient CreateClient()
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
        };
        return new HttpClient(handler);
    }
}
