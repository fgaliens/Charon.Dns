#nullable enable
using System;
using System.Buffers.Binary;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Charon.Dns.Lib.Client;
using Charon.Dns.Lib.Client.RequestResolver;
using Charon.Dns.Lib.Protocol;
using Charon.Dns.Lib.Tracing;
using JetBrains.Annotations;
using Serilog.Core;
using Xunit;

namespace Charon.Dns.Lib.Tests.Client.RequestResolver;

[TestSubject(typeof(StreamRequestResolver))]
public class StreamRequestResolverTest
{
    private static readonly Domain Name = new("ya.ru");
    private static readonly Domain OtherName = new("other.ru");

    [Fact]
    public async Task PipelinedQueries_AreAnsweredOutOfOrderOverOneConnection()
    {
        await using var upstream = new FakeStreamServer(async (stream, _) =>
        {
            var firstRequest = await ReadRequest(stream);
            var secondRequest = await ReadRequest(stream);
            await WriteResponse(stream, secondRequest!);
            await WriteResponse(stream, firstRequest!);
            await ReadRequest(stream);
        });
        using var resolver = new TcpRequestResolver(upstream.EndPoint, Logger.None);
        var firstRequest = CreateRequest(id: 1);
        var secondRequest = CreateRequest(id: 2);

        var firstResolving = resolver.Resolve(firstRequest, RequestTrace.Empty);
        var secondResolving = resolver.Resolve(secondRequest, RequestTrace.Empty);
        var responses = await Task.WhenAll(firstResolving, secondResolving);

        // Internal ids are mapped back to the original ones
        Assert.Equal(1, responses[0].Id);
        Assert.Equal(2, responses[1].Id);
        Assert.Equal(1, upstream.ConnectionCount);
    }

    [Fact]
    public async Task ConnectionClosedWithoutResponse_QueryIsRetried()
    {
        await using var upstream = new FakeStreamServer(async (stream, connectionNumber) =>
        {
            var request = await ReadRequest(stream);
            if (connectionNumber == 1)
            {
                // The server closes the connection, e.g. as idle
                return;
            }

            await WriteResponse(stream, request!);
            await ReadRequest(stream);
        });
        using var resolver = new TcpRequestResolver(upstream.EndPoint, Logger.None);
        var request = CreateRequest(id: 5);

        var response = await resolver.Resolve(request, RequestTrace.Empty);

        Assert.Equal(5, response.Id);
        Assert.Equal(2, upstream.ConnectionCount);
    }

    [Fact]
    public async Task ConnectionClosedByServer_NextQueryOpensNewConnection()
    {
        await using var upstream = new FakeStreamServer(async (stream, _) =>
        {
            var request = await ReadRequest(stream);
            await WriteResponse(stream, request!);
        });
        using var resolver = new TcpRequestResolver(upstream.EndPoint, Logger.None);

        var firstResponse = await resolver.Resolve(CreateRequest(id: 1), RequestTrace.Empty);
        var secondResponse = await resolver.Resolve(CreateRequest(id: 2), RequestTrace.Empty);

        Assert.Equal(1, firstResponse.Id);
        Assert.Equal(2, secondResponse.Id);
        Assert.Equal(2, upstream.ConnectionCount);
    }

    [Fact]
    public async Task ResponseWithOtherQuestion_Fails()
    {
        await using var upstream = new FakeStreamServer(async (stream, _) =>
        {
            var request = await ReadRequest(stream);
            request!.Questions[0] = new Question(OtherName);
            await WriteResponse(stream, request);
            await ReadRequest(stream);
        });
        using var resolver = new TcpRequestResolver(upstream.EndPoint, Logger.None);

        var resolving = resolver.Resolve(CreateRequest(id: 1), RequestTrace.Empty);

        await Assert.ThrowsAsync<ResponseException>(() => resolving);
    }

    [Fact]
    public async Task ServerNotAnswering_QueryTimesOut()
    {
        await using var upstream = new FakeStreamServer(async (stream, _) =>
        {
            await ReadRequest(stream);
            await ReadRequest(stream);
        });
        using var resolver = new TcpRequestResolver(upstream.EndPoint, Logger.None);

        var resolving = resolver.Resolve(CreateRequest(id: 1), RequestTrace.Empty);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => resolving);
    }

    private static Request CreateRequest(int id)
    {
        var request = new Request
        {
            Id = id,
            RecursionDesired = true,
        };
        var question = new Question(Name);
        request.Questions.Add(question);
        return request;
    }

    private static async Task<Request?> ReadRequest(Stream stream)
    {
        var lengthPrefix = new byte[2];
        var prefixLength = await stream.ReadAtLeastAsync(lengthPrefix, 2, throwOnEndOfStream: false);
        if (prefixLength < 2)
        {
            return null;
        }

        var message = new byte[BinaryPrimitives.ReadUInt16BigEndian(lengthPrefix)];
        await stream.ReadExactlyAsync(message);
        return Request.FromArray(message);
    }

    private static async Task WriteResponse(Stream stream, Request request)
    {
        var response = Response.FromRequest(request);
        var message = response.ToArray();
        var frame = new byte[2 + message.Length];
        BinaryPrimitives.WriteUInt16BigEndian(frame, (ushort)message.Length);
        message.CopyTo(frame, 2);
        await stream.WriteAsync(frame);
    }

    /// <summary>
    /// Handles each accepted connection, the connection is closed when the handler returns.
    /// </summary>
    private sealed class FakeStreamServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _cancellation = new();
        private readonly Func<Stream, int, Task> _handleConnection;
        private readonly Task _accepting;
        private int _connectionCount;

        public FakeStreamServer(Func<Stream, int, Task> handleConnection)
        {
            _handleConnection = handleConnection;
            _listener.Start();
            EndPoint = (IPEndPoint)_listener.LocalEndpoint;
            _accepting = Accept();
        }

        public IPEndPoint EndPoint { get; }

        public int ConnectionCount => Volatile.Read(ref _connectionCount);

        public async ValueTask DisposeAsync()
        {
            await _cancellation.CancelAsync();
            _listener.Stop();
            try
            {
                await _accepting;
            }
            catch (OperationCanceledException)
            {
            }

            _cancellation.Dispose();
        }

        private async Task Accept()
        {
            while (true)
            {
                var client = await _listener.AcceptTcpClientAsync(_cancellation.Token);
                var connectionNumber = Interlocked.Increment(ref _connectionCount);
                _ = Handle(client, connectionNumber);
            }
        }

        private async Task Handle(TcpClient client, int connectionNumber)
        {
            using (client)
            {
                try
                {
                    await _handleConnection(client.GetStream(), connectionNumber);
                }
                catch (IOException)
                {
                    // The client is gone
                }
            }
        }
    }
}
