#nullable enable
using System;
using System.Net;
using System.Net.Sockets;
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
using Xunit;

namespace Charon.Dns.Lib.Tests.Server
{
    public class DnsServerResponseTest
    {
        private static readonly TimeSpan ReceiveTimeout = TimeSpan.FromSeconds(2);
        private static readonly Domain Name = new("my.home");
        private static readonly Domain RootName = new("");

        [Fact]
        public async Task MalformedRequest_IsAnsweredWithFormatError()
        {
            await using var server = await TestServer.Start(new FailingResolver());
            // Header announces a question which is cut off
            byte[] message = [0x12, 0x34, 0x01, 0x00, 0, 1, 0, 0, 0, 0, 0, 0, 0xFF];

            var response = await server.Send(message);

            Assert.NotNull(response);
            Assert.Equal(0x1234, response.Id);
            Assert.Equal(ResponseCode.FormatError, response.ResponseCode);
            Assert.True(response.RecursionDesired);
            Assert.True(response.RecursionAvailable);
        }

        [Fact]
        public async Task IncomingResponse_IsNotAnswered()
        {
            await using var server = await TestServer.Start(new FailingResolver());
            byte[] message = [0x12, 0x34, 0x81, 0x80, 0, 0, 0, 0, 0, 0, 0, 0];

            var response = await server.Send(message);

            Assert.Null(response);
        }

        [Fact]
        public async Task ResolverError_IsAnsweredWithServerFailure()
        {
            await using var server = await TestServer.Start(new FailingResolver());
            var request = CreateRequest();
            var message = request.ToArray();

            var response = await server.Send(message);

            Assert.NotNull(response);
            Assert.Equal(request.Id, response.Id);
            Assert.Equal(ResponseCode.ServerFailure, response.ResponseCode);
            Assert.True(response.RecursionAvailable);
        }

        [Fact]
        public async Task ServerFailureForEdnsRequest_CarriesOptRecord()
        {
            await using var server = await TestServer.Start(new FailingResolver());
            var request = CreateRequest();
            var opt = new ResourceRecord(RootName, [], RecordType.OPT, (RecordClass)4096);
            request.AdditionalRecords.Add(opt);
            var message = request.ToArray();

            var response = await server.Send(message);

            Assert.NotNull(response);
            Assert.Equal(ResponseCode.ServerFailure, response.ResponseCode);
            var responseOpt = Assert.Single(response.AdditionalRecords);
            Assert.Equal(RecordType.OPT, responseOpt.Type);
        }

        [Theory]
        [InlineData(null, true)]
        [InlineData(512, true)]
        [InlineData(4096, false)]
        public async Task ResponseLargerThanClientLimit_IsTruncated(int? payloadSize, bool expectTruncated)
        {
            await using var server = await TestServer.Start(new LargeResponseResolver());
            var request = CreateRequest();
            if (payloadSize is not null)
            {
                var opt = new ResourceRecord(RootName, [], RecordType.OPT, (RecordClass)payloadSize.Value);
                request.AdditionalRecords.Add(opt);
            }
            var message = request.ToArray();

            var rawResponse = await server.SendRaw(message);

            var maxResponseSize = DnsServer.GetMaxResponseSize(request);

            Assert.NotNull(rawResponse);
            Assert.True(rawResponse.Length <= maxResponseSize);
            var header = Header.FromArray(rawResponse);
            Assert.Equal(expectTruncated, header.Truncated);
            Assert.Equal(expectTruncated ? 0 : LargeResponseResolver.RecordCount, header.AnswerRecordCount);
            // OPT record is kept in a truncated response
            Assert.Equal(payloadSize is null ? 0 : 1, header.AdditionalRecordCount);
        }

        [Theory]
        [InlineData(null, 512)]
        [InlineData(100, 512)]
        [InlineData(1232, 1232)]
        [InlineData(65535, 65535)]
        public void GetMaxResponseSize_UsesEdnsPayloadSize(int? payloadSize, int expected)
        {
            var request = CreateRequest();
            if (payloadSize is not null)
            {
                var opt = new ResourceRecord(RootName, [], RecordType.OPT, (RecordClass)payloadSize.Value);
                request.AdditionalRecords.Add(opt);
            }

            var maxResponseSize = DnsServer.GetMaxResponseSize(request);

            Assert.Equal(expected, maxResponseSize);
        }

        private static Request CreateRequest()
        {
            var request = new Request
            {
                Id = 4242,
                RecursionDesired = true,
            };
            var question = new Question(Name, RecordType.TXT);
            request.Questions.Add(question);

            return request;
        }

        private sealed class TestServer : IAsyncDisposable
        {
            private readonly CancellationTokenSource _cancellation = new();
            private readonly IPEndPoint _endPoint;
            private readonly Task _listening;

            private TestServer(IRequestResolver resolver)
            {
                var port = GetFreePort();
                _endPoint = new IPEndPoint(IPAddress.Loopback, port);
                var requestCounter = new RequestCounter();
                var socketBufferSize = new ByteUnit(64 * 1024);
                var server = new DnsServer(resolver, requestCounter, 1, socketBufferSize, Logger.None,
                    recursionAvailable: true);
                _listening = server.Listen(_endPoint, enableIpV6: false, _cancellation.Token);
            }

            public static async Task<TestServer> Start(IRequestResolver resolver)
            {
                var server = new TestServer(resolver);
                // Let the listening socket bind
                await Task.Delay(100);
                return server;
            }

            public async Task<Response?> Send(byte[] message)
            {
                var rawResponse = await SendRaw(message);
                return rawResponse is null ? null : Response.FromArray(rawResponse);
            }

            public async Task<byte[]?> SendRaw(byte[] message)
            {
                using var client = new UdpClient(AddressFamily.InterNetwork);
                await client.SendAsync(message, _endPoint);

                using var timeout = new CancellationTokenSource(ReceiveTimeout);
                try
                {
                    var result = await client.ReceiveAsync(timeout.Token);
                    return result.Buffer;
                }
                catch (OperationCanceledException)
                {
                    return null;
                }
            }

            public async ValueTask DisposeAsync()
            {
                _cancellation.Cancel();
                try
                {
                    await _listening;
                }
                catch (OperationCanceledException)
                {
                }
            }

            private static int GetFreePort()
            {
                using var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
                return ((IPEndPoint)socket.Client.LocalEndPoint!).Port;
            }
        }

        private sealed class FailingResolver : IRequestResolver
        {
            public Task<IResponse> Resolve(IRequest request, RequestTrace trace, CancellationToken cancellationToken = default)
            {
                throw new InvalidOperationException("Upstream is not available");
            }
        }

        private sealed class LargeResponseResolver : IRequestResolver
        {
            public const int RecordCount = 10;

            public Task<IResponse> Resolve(IRequest request, RequestTrace trace, CancellationToken cancellationToken = default)
            {
                var response = Response.FromRequest(request);
                var text = new string('x', 200);
                var characterStrings = CharacterString.FromString(text);
                for (var i = 0; i < RecordCount; i++)
                {
                    var record = new TextResourceRecord(Name, characterStrings, TimeSpan.FromMinutes(1));
                    response.AnswerRecords.Add(record);
                }

                foreach (var additionalRecord in request.AdditionalRecords)
                {
                    response.AdditionalRecords.Add(additionalRecord);
                }

                return Task.FromResult<IResponse>(response);
            }
        }
    }
}
