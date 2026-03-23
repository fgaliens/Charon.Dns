#nullable enable
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Charon.Dns.Lib.Protocol;
using Serilog;

namespace Charon.Dns.Lib.Client.RequestResolver
{
    public class UdpRequestResolver : IRequestResolver
    {
        private const int MaxUdpMsgSize = 512;
        
        private readonly TimeSpan _timeout = TimeSpan.FromSeconds(2);
        private readonly IPEndPoint _dnsEndpoint;
        private readonly IRequestResolver? _fallback;
        private readonly ILogger? _logger;
        private readonly ConcurrentBag<Socket> _availableSockets = new();
        private readonly ArrayPool<byte> _arrayPool = ArrayPool<byte>.Shared;

        public UdpRequestResolver(
            IPEndPoint dnsEndpoint, 
            IRequestResolver? fallback = null, 
            ILogger? logger = null)
        {
            _dnsEndpoint = dnsEndpoint;
            _fallback = fallback;
            _logger = logger;
        }

        public async Task<IResponse> Resolve(
            IRequest request, 
            IPEndPoint remoteEndPoint, 
            CancellationToken cancellationToken = default)
        {
            if (!_availableSockets.TryTake(out var socket))
            {
                socket = new Socket(SocketType.Dgram, ProtocolType.Udp);
                _logger?.Debug($"{nameof(UdpRequestResolver)}: New socket for DNS {{Ip}} created", _dnsEndpoint);
            }

            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var isDebugging = false;
#if DEBUG
                isDebugging = Debugger.IsAttached;
#endif
                if (!isDebugging)
                {
                    cts.CancelAfter(_timeout);
                }

                var requestData = request.ToArray();
                await socket.SendToAsync(requestData, SocketFlags.None, _dnsEndpoint, cts.Token);

                var buffer = _arrayPool.Rent(MaxUdpMsgSize);

                IResponse? response = null;
                while (request.Id != response?.Id)
                {
                    if (response is not null)
                    {
                        _logger?.Warning($"{nameof(UdpRequestResolver)}: DNS resolver ({{Ip}}) got response with unexpected ID:\n" +
                            "Request: {@Request}\nResponse: {@Response}",
                            _dnsEndpoint, request, response);
                    }

                    var responseInfo = await socket.ReceiveFromAsync(buffer, _dnsEndpoint, cts.Token);
                    var senderIp = (responseInfo.RemoteEndPoint as IPEndPoint)?.Address.MapToIPv6();
                    if (!_dnsEndpoint.Address.MapToIPv6().Equals(senderIp))
                    {
                        throw new IOException($"Remote endpoint mismatch. Expected response from {_dnsEndpoint}, received from {responseInfo.RemoteEndPoint}");
                    }

                    response = Response.FromArray(buffer[..responseInfo.ReceivedBytes]);
                }

                _arrayPool.Return(buffer);

                if (response.Truncated)
                {
                    if (_fallback != null)
                    {
                        return await _fallback.Resolve(request, remoteEndPoint, cts.Token);
                    }
                    _logger?.Warning($"{nameof(UdpRequestResolver)}: DNS resolver ({{Ip}}) got truncated response for request: {{@Request}}",
                        _dnsEndpoint, request);
                }
                return response;
            }
            catch (OperationCanceledException operationCanceledException)
            {
                throw new OperationCanceledException($"Request to {request.Questions[0].Name} (DNS: {_dnsEndpoint} was canceled)", operationCanceledException);
            }
            catch (Exception e)
            {
                _logger?.Error(e, $"{nameof(UdpRequestResolver)}: DNS resolver ({{Ip}}) got error for request {{@Request}}",
                    _dnsEndpoint, request);
                throw;
            }
            finally
            {
                _availableSockets.Add(socket);
                _logger?.Debug($"{nameof(UdpRequestResolver)}: Socket for DNS {{Ip}} returned to pool. Pool size: {{Size}}", 
                    _dnsEndpoint, _availableSockets.Count);
            }
        }
    }
}
