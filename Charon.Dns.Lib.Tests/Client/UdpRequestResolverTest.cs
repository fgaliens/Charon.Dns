#nullable enable
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Charon.Dns.Lib.Client.RequestResolver;
using Charon.Dns.Lib.Protocol;
using Charon.Dns.Lib.Tracing;
using Serilog.Core;
using Xunit;

namespace Charon.Dns.Lib.Tests.Client
{
    public class UdpRequestResolverTest
    {
        [Fact]
        public async Task Resolve_AfterMalformedResponse_KeepsReceivingResponses()
        {
            using var upstream = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            var upstreamEndPoint = (IPEndPoint)upstream.Client.LocalEndPoint!;
            using var resolver = new UdpRequestResolver(upstreamEndPoint, null, Logger.None);
            var upstreamTask = AnswerWithMalformedThenValidResponse(upstream);

            var request = CreateRequest();
            var response = await resolver.Resolve(request, RequestTrace.Empty);
            await upstreamTask;

            Assert.Equal(request.Id, response.Id);
            Assert.Equal(ResponseCode.NoError, response.ResponseCode);
            var question = Assert.Single(response.Questions);
            Assert.Equal("ya.ru", question.Name.ToString());
        }

        [Fact]
        public async Task Resolve_ResponseFromAnotherAddress_IsDropped()
        {
            using var upstream = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            using var spoofer = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            var upstreamEndPoint = (IPEndPoint)upstream.Client.LocalEndPoint!;
            using var resolver = new UdpRequestResolver(upstreamEndPoint, null, Logger.None);
            var upstreamTask = AnswerWithSpoofedThenValidResponse(upstream, spoofer);

            var request = CreateRequest();
            var response = await resolver.Resolve(request, RequestTrace.Empty);
            await upstreamTask;

            Assert.Equal(ResponseCode.NoError, response.ResponseCode);
        }

        private static async Task AnswerWithSpoofedThenValidResponse(UdpClient upstream, UdpClient spoofer)
        {
            var received = await upstream.ReceiveAsync();
            var requestMessage = received.Buffer;

            // Same id and question, but it comes from another port
            var spoofedResponse = (byte[])requestMessage.Clone();
            spoofedResponse[2] |= 0x80;
            spoofedResponse[3] = 0x80 | (byte)ResponseCode.NameError;
            await spoofer.SendAsync(spoofedResponse, received.RemoteEndPoint);
            await Task.Delay(100);

            var validResponse = (byte[])requestMessage.Clone();
            validResponse[2] |= 0x80;
            validResponse[3] = 0x80;
            await upstream.SendAsync(validResponse, received.RemoteEndPoint);
        }

        private static async Task AnswerWithMalformedThenValidResponse(UdpClient upstream)
        {
            var received = await upstream.ReceiveAsync();
            var requestMessage = received.Buffer;

            // Header announces a question which is cut off
            byte[] malformedResponse = [requestMessage[0], requestMessage[1], 0x81, 0x80, 0, 1, 0, 0, 0, 0, 0, 0, 0xFF];
            await upstream.SendAsync(malformedResponse, received.RemoteEndPoint);

            var validResponse = (byte[])requestMessage.Clone();
            validResponse[2] |= 0x80;
            validResponse[3] = 0x80;
            await upstream.SendAsync(validResponse, received.RemoteEndPoint);
        }

        private static Request CreateRequest()
        {
            var request = new Request
            {
                RecursionDesired = true,
            };
            var name = new Domain("ya.ru");
            var question = new Question(name, RecordType.A);
            request.Questions.Add(question);

            return request;
        }
    }
}
