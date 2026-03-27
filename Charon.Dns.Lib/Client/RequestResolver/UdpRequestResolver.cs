#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Charon.Dns.Lib.Protocol;
using Charon.Dns.Lib.Protocol.Utils;
using Charon.Dns.Lib.Tracing;
using Serilog;

namespace Charon.Dns.Lib.Client.RequestResolver;

public class UdpRequestResolver : IRequestResolver, IDisposable
{
    private const int MaxUdpMsgSize = 512;
    
    private readonly TimeSpan _timeout = TimeSpan.FromSeconds(2);
    private readonly IPEndPoint _dnsEndpoint;
    private readonly ILogger _globalLogger;
    private readonly List<Socket> _sockets = new();
    private readonly ConcurrentDictionary<int, UnhandledRequest> _requestsMap = new();
    private readonly ConcurrentQueue<UnhandledRequest> _deferredRequests = new();
    //private readonly ArrayPool<byte> _arrayPool = ArrayPool<byte>.Shared;
    private readonly CancellationTokenSource _resolverWorkCancellation = new();
    private ulong _requestsCount;
    private bool _disposed;

    public UdpRequestResolver(
        IPEndPoint dnsEndpoint, 
        ILogger globalLogger)
    {
        _dnsEndpoint = dnsEndpoint;
        _globalLogger = globalLogger;
        
        // var taskFactory = new TaskFactory(_resolverWorkCancellation.Token);
        // _ = taskFactory.StartNew(async () => await RequestHandlingLoop(), TaskCreationOptions.LongRunning);
        for (int i = 0; i < 1; i++)
        {
            var socket = new Socket(SocketType.Dgram, ProtocolType.Udp);
            socket.Bind(new  IPEndPoint(0, 0));
            socket.ReceiveBufferSize = 5 * 1024 * 1024;
            socket.SendBufferSize = 5 * 1024 * 1024;
            _sockets.Add(socket);
            _ = Task.Run(async () =>
            {
                var localSocket = socket;
                await RequestHandlingLoop(localSocket);
            }, _resolverWorkCancellation.Token);
        }
    }

    public async Task<IResponse> Resolve(
        IRequest request, 
        RequestTrace trace, 
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        
        var requestTask = new TaskCompletionSource<IResponse>();
        var unhandledRequest = new UnhandledRequest
        {
            Request = request,
            ResponsePromise = requestTask,
            Trace = trace,
        };

        if (_requestsMap.TryAdd(request.Id, unhandledRequest))
        {
            var requestId = Interlocked.Increment(ref _requestsCount);
            var socket = _sockets[(int)requestId % _sockets.Count];
            
            await SendRequestToDnsServer(socket, request, cancellationToken);
            trace.Logger.Debug("Request {@Request} sent to DNS server {Dns}", request, _dnsEndpoint);
        }
        else
        {
            _deferredRequests.Enqueue(unhandledRequest);
            trace.Logger.Debug("Request {@Request} deferred. Waiting to send to DNS server {Dns}", request, _dnsEndpoint);
        }
        trace.Logger.Debug("Active requests: {ActiveCount}; deferred: {DeferredCount}", 
            _requestsMap.Count, _deferredRequests.Count);

        try
        {
            return await requestTask.Task.WithCancellationTimeout(_timeout, cancellationToken);
        }
        catch (Exception e)
        {
            _requestsMap.TryRemove(request.Id, out _);
            trace.Logger.Error(e, "{Source}. Resolving request error via DNS {Dns}! Request: {@Request}",
                nameof(UdpReceiveResult), _dnsEndpoint, request);
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _resolverWorkCancellation.Cancel();
    }

    private async Task RequestHandlingLoop(Socket socket)
    {
        using (socket)
        {
            var buffer = new byte[MaxUdpMsgSize * 2];
            while (!_resolverWorkCancellation.IsCancellationRequested)
            {
                try
                {
                    var responseInfo = await socket.ReceiveFromAsync(buffer, _dnsEndpoint, _resolverWorkCancellation.Token);

                    var msgSize = responseInfo.ReceivedBytes;
                    if (msgSize == 0)
                    {
                        continue;
                    }

                    var response = Response.FromArray(buffer[..responseInfo.ReceivedBytes]);

                    if (_requestsMap.TryRemove(response.Id, out var requestInfo))
                    {
                        var logger = requestInfo.Trace.Logger;
                        try
                        {
                            if (msgSize >= MaxUdpMsgSize * 2)
                            {
                                logger.Error("Received message has unexpected size: {Size} bytes", msgSize);
                            }
                            else if (msgSize > MaxUdpMsgSize)
                            {
                                logger.Warning("Received message has unexpected size: {Size} bytes", msgSize);
                            }

                            var senderIp = (responseInfo.RemoteEndPoint as IPEndPoint)?.Address.MapToIPv6();
                            if (!_dnsEndpoint.Address.MapToIPv6().Equals(senderIp))
                            {
                                logger.Warning("Remote endpoint mismatch. Response dropped. Expected response from {DNS}, received from {RemoteEndPoint}",
                                    _dnsEndpoint, senderIp);

                                continue;
                            }

                            var resultSet = requestInfo.ResponsePromise.TrySetResult(response);

                            if (resultSet)
                            {
                                logger.Debug("Response handled via {Dns}", _dnsEndpoint);
                            }
                            else
                            {
                                logger.Error("Response handled via {Dns} with problems. Result not set", _dnsEndpoint);
                            }

                            await AddRequestFromUnuniqueItemsQueue(socket);
                        }
                        catch (Exception e)
                        {
                            logger.Error(e, "Udp request resolver error (DNS {Dns}). Matched response was found",
                                socket.LocalEndPoint);
                            requestInfo.ResponsePromise.SetException(e);
                        }
                    }
                    else
                    {
                        _globalLogger.Warning("Request for response {@Response} wasn't found. Response dropped ({Dns})",
                            response, _dnsEndpoint);
                    }
                }
                catch (Exception e)
                {
                    _globalLogger.Error(e, "Udp request resolver error (DNS {Dns})", socket.LocalEndPoint);
                }
                finally
                {
                    await Task.Yield();
                }
            }
        }
    }

    private async ValueTask SendRequestToDnsServer(
        Socket socket, 
        IRequest request, 
        CancellationToken cancellationToken)
    {
        var requestData = request.ToArray();
        await socket.SendToAsync(requestData, SocketFlags.None, _dnsEndpoint, cancellationToken);
    }
    
    private async ValueTask AddRequestFromUnuniqueItemsQueue(Socket socket)
    {
        while (!_resolverWorkCancellation.IsCancellationRequested)
        {
            if (!_deferredRequests.TryPeek(out var request))
            {
                return;
            }

            if (_requestsMap.ContainsKey(request.Request.Id))
            {
                return;
            }

            if (_requestsMap.TryAdd(request.Request.Id, request))
            {
                _globalLogger.Debug("Sending deferred request {@Request} for DNS server {Dns}", 
                    request, _dnsEndpoint);
                
                _deferredRequests.TryDequeue(out _);
                await SendRequestToDnsServer(socket, request.Request, _resolverWorkCancellation.Token);
            }
        }
    }
    
    private readonly record struct UnhandledRequest
    {
        public required IRequest Request { get; init; }
        public required TaskCompletionSource<IResponse> ResponsePromise { get; init; }
        public required RequestTrace Trace { get; init; }
    }
}
