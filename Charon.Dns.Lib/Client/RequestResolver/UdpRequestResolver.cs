#nullable enable
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Charon.Dns.Lib.Concurrency;
using Charon.Dns.Lib.Protocol;
using Charon.Dns.Lib.Tracing;

namespace Charon.Dns.Lib.Client.RequestResolver;

public class UdpRequestResolver : IRequestResolver
{
    private const int MaxDnsMsgSize = 512;
    
    private readonly TimeSpan _timeout = TimeSpan.FromSeconds(2);
    private readonly IPEndPoint _dnsEndpoint;
    private readonly IRequestResolver? _fallback;
    private readonly ConcurrentQueue<Socket> _availableSockets = new();
    private readonly ArrayPool<byte> _arrayPool = ArrayPool<byte>.Shared;
    private readonly IConcurrencyLimiter _concurrencyLimiter;
    private readonly int _concurrencyLimit;
    private ulong _returnedSocketsCounter;

    public UdpRequestResolver(
        IPEndPoint dnsEndpoint, 
        int concurrencyLimit,
        IRequestResolver? fallback = null)
    {
        _dnsEndpoint = dnsEndpoint;
        _fallback = fallback;
        _concurrencyLimit = concurrencyLimit;
        _concurrencyLimiter = concurrencyLimit > 0
            ? new ConcurrencyLimiter(concurrencyLimit)
            : EmptyConcurrencyLimiter.Instance;
    }

    public async Task<IResponse> Resolve(
        IRequest request, 
        RequestTrace trace, 
        CancellationToken cancellationToken = default)
    {
        using var limiterScope = await _concurrencyLimiter.WaitAsync(cancellationToken);
        var logger = trace.Logger;
        
        if (!_availableSockets.TryDequeue(out var socket))
        {
            socket = new Socket(SocketType.Dgram, ProtocolType.Udp);
            logger.Debug(
                $"{nameof(UdpRequestResolver)}: New socket {{Socket}} for DNS {{Ip}} created",
                socket.LocalEndPoint,
                _dnsEndpoint);
        }
        
        logger.Debug(
            $"{nameof(UdpRequestResolver)}: Using socket {{Socket}} for DNS {{Ip}}. Av. data {{DataSize}} bytes",
            socket.LocalEndPoint,
            _dnsEndpoint,
            socket.Available);

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

            var dnsMsgSize = MaxDnsMsgSize;
            if (request.AdditionalRecords.Count > 0)
            {
                foreach (var additionalRecord in request.AdditionalRecords)
                {
                    if (additionalRecord.Type is RecordType.OPT)
                    {
                        dnsMsgSize = 4096;
                        logger.Debug("OPT additional record found: {Record}", additionalRecord);
                        break;
                    }
                }
            }

            var requestData = request.ToArray();
            await socket.SendToAsync(requestData, SocketFlags.None, _dnsEndpoint, cts.Token);

            var buffer = _arrayPool.Rent(dnsMsgSize * 2);

            IResponse? response = null;
            var loopsCount = 0;
            const int maxLoopsCount = 10;
            while (request.Id != response?.Id)
            {
                if (response is not null)
                {
                    loopsCount++;
                    
                    logger.Warning($"{nameof(UdpRequestResolver)}: DNS resolver ({{Ip}}) got response with unexpected ID ({{LoopCount}}):\n" +
                        "Request: {@Request}\nResponse: {@Response}",
                        _dnsEndpoint, loopsCount, request, response);
                    
                    if (loopsCount >= maxLoopsCount)
                    {
                        throw new InvalidOperationException("Unexpected response id");
                    }
                }

                var availableDataSize = socket.Available;
                logger.Debug("Socket {Socket} has available data: {DataSize} bytes", socket.LocalEndPoint, availableDataSize);

                var responseInfo = await socket.ReceiveFromAsync(buffer, _dnsEndpoint, cts.Token);

                var msgSize = responseInfo.ReceivedBytes;
                if (msgSize > MaxDnsMsgSize * 2)
                {
                    logger.Error("Received message has unexpected size: {Size} bytes", msgSize);
                }
                else if (msgSize > MaxDnsMsgSize)
                {
                    logger.Warning("Received message has unexpected size: {Size} bytes", msgSize);
                }
                
                var senderIp = (responseInfo.RemoteEndPoint as IPEndPoint)?.Address.MapToIPv6();
                if (!_dnsEndpoint.Address.MapToIPv6().Equals(senderIp))
                {
                    logger.Warning("Remote endpoint mismatch. Expected response from {DNS}, received from {RemoteEndPoint}. (ID: {TraceId})",
                        _dnsEndpoint, senderIp, request.Id);
                    continue;
                }

                response = Response.FromArray(buffer[..responseInfo.ReceivedBytes]);
            }

            _arrayPool.Return(buffer);

            if (response.Truncated)
            {
                if (_fallback != null)
                {
                    return await _fallback.Resolve(request, trace, cts.Token);
                }
                logger.Warning($"{nameof(UdpRequestResolver)}: DNS resolver ({{Ip}}) got truncated response for request: {{@Request}}",
                    _dnsEndpoint, request);
            }
            return response;
        }
        catch (OperationCanceledException operationCanceledException)
        {
            throw new OperationCanceledException(
                $"Request for domain '{request.Questions[0].Name}' to (DNS: {_dnsEndpoint} was canceled)", 
                operationCanceledException);
        }
        catch (Exception e)
        {
            logger.Error(e, $"{nameof(UdpRequestResolver)}: DNS resolver ({{Ip}}) got error for request {{@Request}}",
                _dnsEndpoint, request);
            throw;
        }
        finally
        {
            _availableSockets.Enqueue(socket);
            FreeSocketsIfNeeded(trace);
            
            logger.Debug($"{nameof(UdpRequestResolver)}: Socket for DNS {{Ip}} returned to pool. Pool size: {{Size}}", 
                _dnsEndpoint, _availableSockets.Count);
        }
    }

    private void FreeSocketsIfNeeded(RequestTrace trace)
    {
        if (_concurrencyLimit <= 0)
        {
            return;
        }
        
        var counter = Interlocked.Increment(ref _returnedSocketsCounter);
        if (counter % 1000 != 0)
        {
            return;
        }

        Interlocked.Exchange(ref _returnedSocketsCounter, 1);
        
        if (_availableSockets.Count <= 5)
        {
            return;
        }
        
        const double socketsFreeFactor = 0.8;
        var loger = trace.Logger;
        
        var targetSocketsCount = (int)(_availableSockets.Count * socketsFreeFactor);
        
        loger.Debug("Trying to free sockets for DNS '{Dns}'. Current count: {SocketsCount}, target count: {TargetSocketsCount}",
            _dnsEndpoint, _availableSockets.Count, targetSocketsCount);
        
        while (_availableSockets.Count > targetSocketsCount)
        {
            if (_availableSockets.TryDequeue(out var socket))
            {
                loger.Debug("Disposing connection to DNS '{Dns}'. Socket {Socket}",  _dnsEndpoint, socket.LocalEndPoint);
                socket.Dispose();
            }
        }
    }
}
