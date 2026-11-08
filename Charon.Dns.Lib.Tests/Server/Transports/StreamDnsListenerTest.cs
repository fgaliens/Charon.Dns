#nullable enable
using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Net.Security;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Charon.Dns.Lib.Client.RequestResolver;
using Charon.Dns.Lib.Protocol;
using Charon.Dns.Lib.Protocol.ResourceRecords;
using Charon.Dns.Lib.Server.Transports;
using Charon.Dns.Lib.Tracing;
using JetBrains.Annotations;
using Xunit;

namespace Charon.Dns.Lib.Tests.Server.Transports;

[TestSubject(typeof(StreamDnsListener))]
public class StreamDnsListenerTest
{
    private const ushort PaddingOptionCode = 12;
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(5);
    private static readonly Domain RootName = new("");

    [Fact]
    public async Task Tcp_ResponseIsNotTruncated()
    {
        await using var server = await TransportTestServer.StartTcp();
        var resolver = new TcpRequestResolver(server.EndPoint);
        var request = TransportTestServer.CreateRequest(TransportTestServer.Name);

        var response = await resolver.Resolve(request, RequestTrace.Empty);

        Assert.Equal(request.Id, response.Id);
        Assert.False(response.Truncated);
        Assert.Equal(10, response.AnswerRecords.Count);
    }

    [Fact]
    public async Task Tcp_ResolverError_IsAnsweredWithServerFailure()
    {
        await using var server = await TransportTestServer.StartTcp();
        var resolver = new TcpRequestResolver(server.EndPoint);
        var request = TransportTestServer.CreateRequest(TransportTestServer.FailingName);

        var response = await resolver.Resolve(request, RequestTrace.Empty);

        Assert.Equal(ResponseCode.ServerFailure, response.ResponseCode);
    }

    [Fact]
    public async Task Tcp_PipelinedRequests_AreAnsweredWhenResolved()
    {
        await using var server = await TransportTestServer.StartTcp();
        using var client = new TcpClient();
        await client.ConnectAsync(server.EndPoint);
        var stream = client.GetStream();
        var slowRequest = TransportTestServer.CreateRequest(TransportTestServer.SlowName, id: 1);
        var fastRequest = TransportTestServer.CreateRequest(TransportTestServer.Name, id: 2);

        await WriteMessage(stream, slowRequest);
        await WriteMessage(stream, fastRequest);
        var firstResponse = await ReadMessage(stream);
        var secondResponse = await ReadMessage(stream);

        // RFC 7766, 6.2.1.1: the slow query doesn't delay the fast one
        Assert.Equal(2, firstResponse!.Id);
        Assert.Equal(1, secondResponse!.Id);
    }

    [Fact]
    public async Task Tcp_MessageShorterThanHeader_ClosesConnection()
    {
        await using var server = await TransportTestServer.StartTcp();
        using var client = new TcpClient();
        await client.ConnectAsync(server.EndPoint);
        var stream = client.GetStream();

        byte[] frame = [0, 5, 1, 2, 3, 4, 5];
        await stream.WriteAsync(frame);
        var response = await ReadMessage(stream);

        Assert.Null(response);
    }

    [Fact]
    public async Task Tcp_IncomingResponse_IsNotAnswered()
    {
        await using var server = await TransportTestServer.StartTcp();
        using var client = new TcpClient();
        await client.ConnectAsync(server.EndPoint);
        var stream = client.GetStream();
        byte[] incomingResponse = [0x12, 0x34, 0x81, 0x80, 0, 0, 0, 0, 0, 0, 0, 0];
        var request = TransportTestServer.CreateRequest(TransportTestServer.Name, id: 7);

        await WriteFrame(stream, incomingResponse);
        await WriteMessage(stream, request);
        var response = await ReadMessage(stream);

        // The connection is kept, only the incoming response is ignored
        Assert.Equal(7, response!.Id);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Tls_PadsResponseOnlyIfRequested(bool padRequest)
    {
        using var certificate = TransportTestServer.CreateCertificate();
        await using var server = await TransportTestServer.StartTls(certificate);
        using var client = new TcpClient();
        await client.ConnectAsync(server.EndPoint);
        await using var stream = new SslStream(client.GetStream(), leaveInnerStreamOpen: false, (_, _, _, _) => true);
        var options = new SslClientAuthenticationOptions
        {
            TargetHost = "localhost",
            ApplicationProtocols = [new SslApplicationProtocol("dot")],
        };
        await stream.AuthenticateAsClientAsync(options);
        var request = TransportTestServer.CreateRequest(TransportTestServer.Name);
        // A padding option of the request is empty
        var optData = padRequest ? new byte[] { 0, (byte)PaddingOptionCode, 0, 0 } : [];
        var opt = new ResourceRecord(RootName, optData, RecordType.OPT, (RecordClass)1232);
        request.AdditionalRecords.Add(opt);

        Assert.Equal("dot", stream.NegotiatedApplicationProtocol.ToString());
        await WriteMessage(stream, request);
        var rawResponse = await ReadRawMessage(stream);

        Assert.NotNull(rawResponse);
        var response = Response.FromArray(rawResponse);
        var responseOpt = Assert.Single(response.AdditionalRecords);
        var hasPadding = responseOpt.Data.Length >= 4
            && BinaryPrimitives.ReadUInt16BigEndian(responseOpt.Data) == PaddingOptionCode;
        Assert.Equal(padRequest, hasPadding);
        if (padRequest)
        {
            // RFC 8467, 4.1: responses are padded to a multiple of 468 bytes
            Assert.Equal(0, rawResponse.Length % 468);
        }
    }

    private static Task WriteMessage(Stream stream, Request request)
    {
        var message = request.ToArray();
        return WriteFrame(stream, message);
    }

    private static async Task WriteFrame(Stream stream, byte[] message)
    {
        var frame = new byte[2 + message.Length];
        BinaryPrimitives.WriteUInt16BigEndian(frame, (ushort)message.Length);
        message.CopyTo(frame, 2);
        await stream.WriteAsync(frame);
    }

    private static async Task<Response?> ReadMessage(Stream stream)
    {
        var message = await ReadRawMessage(stream);
        return message is null ? null : Response.FromArray(message);
    }

    /// <returns>Null if the server closed the connection.</returns>
    private static async Task<byte[]?> ReadRawMessage(Stream stream)
    {
        using var timeout = new CancellationTokenSource(ReadTimeout);
        var prefix = new byte[2];
        var prefixLength = await stream.ReadAtLeastAsync(prefix, 2, throwOnEndOfStream: false, timeout.Token);
        if (prefixLength < 2)
        {
            return null;
        }

        var length = BinaryPrimitives.ReadUInt16BigEndian(prefix);
        var message = new byte[length];
        await stream.ReadExactlyAsync(message, timeout.Token);
        return message;
    }
}
