#nullable enable
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Charon.Dns.Lib.Extensions;
using Charon.Dns.Lib.Protocol;
using Charon.Dns.Lib.Protocol.EqualityComparers;
using Charon.Dns.Lib.Tracing;
using Charon.Dns.Utils.Units;
using Serilog;

namespace Charon.Dns.Lib.Client.RequestResolver;

// TODO: Refactoring
public class UdpRequestResolver : IRequestResolver, IDisposable
{
    private const int DefaultDnsMsgSize = 512;
    private const int MaxRequestIdAttempts = 16;

    private readonly TimeSpan _timeout = TimeSpan.FromSeconds(2);
    private readonly IPEndPoint _dnsEndpoint;
    private readonly IPAddress _dnsAddress;
    private readonly ILogger _globalLogger;
    private readonly ArrayPool<byte> _arrayPool = ArrayPool<byte>.Shared;
    private readonly ConcurrentDictionary<ushort, ResponsePromise> _pendingRequests = new();
    private readonly Socket _socket;
    private readonly CancellationTokenSource _resolvingCancellationToken;

    public UdpRequestResolver(
        IPEndPoint dnsEndpoint,
        ByteUnit? socketBufferSize,
        ILogger globalLogger)
    {
        _dnsEndpoint = dnsEndpoint;
        _dnsAddress = Normalize(dnsEndpoint.Address);
        _globalLogger = globalLogger;

        _resolvingCancellationToken = new CancellationTokenSource();

        var socketBufferSizeBytes = socketBufferSize?.Bytes;
        var socket = new Socket(SocketType.Dgram, ProtocolType.Udp);
        socket.ExclusiveAddressUse = true;
        if (socketBufferSizeBytes is not null)
        {
            socket.SendBufferSize = socketBufferSizeBytes.Value;
            socket.ReceiveBufferSize = socketBufferSizeBytes.Value;
        }
        socket.Bind(new IPEndPoint(0, 0));
        _socket = socket;

        globalLogger.Information("UDP request resolver for DNS {DNS} created. Socket: {Socket}. UDP buffer: {UdpBuffer} bytes",
            _dnsEndpoint,
            _socket.LocalEndPoint,
            socketBufferSizeBytes);

        _ = Task.Run(async () =>
        {
            while (!_resolvingCancellationToken.IsCancellationRequested)
            {
                try
                {
                    await HandleRequestFromSocket();
                }
                catch (Exception e) when (!_resolvingCancellationToken.IsCancellationRequested)
                {
                    // A single malformed datagram must not stop receiving of the following responses
                    _globalLogger.Warning(e, "Request resolving (resolver {Resolver}). Unable to handle response. Response dropped",
                        _dnsEndpoint);
                }
            }
        });
    }

    public async Task<IResponse> Resolve(
        IRequest request,
        RequestTrace trace,
        CancellationToken cancellationToken = default)
    {
        var logger = trace.Logger;

        using var linkedCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(
            _resolvingCancellationToken.Token,
            cancellationToken);
        linkedCancellationTokenSource.CancelAfter(_timeout);
        var linkedCancellationToken = linkedCancellationTokenSource.Token;

        var originalRequestId = (ushort)request.Id;
        var promise = RegisterPromise(request, trace, originalRequestId);
        var internalRequestId = promise.InternalRequestId;

        logger.Debug("Request resolving. Mapping request id {ExtId} -> {IntId}", originalRequestId, internalRequestId);

        try
        {
            await using var cancellationRegistration = linkedCancellationToken.Register(() =>
                promise.ResponseCompletionSource.TrySetCanceled(linkedCancellationToken));

            await SendRequest(request, internalRequestId, linkedCancellationToken);

            return await promise.ResponseCompletionSource.Task;
        }
        catch (Exception e)
        {
            logger.Error(e, "Request resolving error!. External id: {ExtId}; internal id: {IntId}", originalRequestId, internalRequestId);
            throw;
        }
        finally
        {
            var ownEntry = new KeyValuePair<ushort, ResponsePromise>(internalRequestId, promise);
            _pendingRequests.TryRemove(ownEntry);
        }
    }

    public void Dispose()
    {
        if (_resolvingCancellationToken.IsCancellationRequested)
        {
            return;
        }

        _resolvingCancellationToken.Cancel();
        _resolvingCancellationToken.Dispose();
        _socket.Dispose();
    }

    /// <summary>
    /// The promise is registered before the request is sent, so a fast response can't outrun it.
    /// A random id makes it hard to spoof a response.
    /// </summary>
    private ResponsePromise RegisterPromise(IRequest request, RequestTrace trace, ushort originalRequestId)
    {
        for (var i = 0; i < MaxRequestIdAttempts; i++)
        {
            var internalRequestId = (ushort)RandomNumberGenerator.GetInt32(ushort.MaxValue + 1);
            var promise = new ResponsePromise
            {
                OriginalRequestId = originalRequestId,
                InternalRequestId = internalRequestId,
                Request = request,
                Trace = trace,
                ResponseCompletionSource = new TaskCompletionSource<IResponse>(),
            };

            if (_pendingRequests.TryAdd(internalRequestId, promise))
            {
                return promise;
            }
        }
        
        // TODO: retry
        throw new InvalidOperationException($"Unable to allocate request id, {_pendingRequests.Count} requests are pending");
    }

    private async Task SendRequest(IRequest request, ushort internalRequestId, CancellationToken cancellationToken)
    {
        var requestSize = request.Size;
        var requestBuffer = _arrayPool.Rent(requestSize);

        try
        {
            request.WriteTo(requestBuffer.AsSpan(0, requestSize));
            var requestMemory = requestBuffer.AsMemory(0, requestSize);
            var messageHeader = requestMemory.DnsMessage.Header;
            messageHeader.Id = internalRequestId;

            await _socket.SendToAsync(
                requestMemory,
                SocketFlags.None,
                _dnsEndpoint,
                cancellationToken);
        }
        finally
        {
            _arrayPool.Return(requestBuffer);
        }
    }

    private async Task HandleRequestFromSocket()
    {
        var cancellationToken = _resolvingCancellationToken.Token;
        var buffer = _arrayPool.Rent(DefaultDnsMsgSize * 8);
        try
        {
            var responseInfo = await _socket.ReceiveFromAsync(buffer, _dnsEndpoint, cancellationToken);
            var remoteEndPoint = (IPEndPoint)responseInfo.RemoteEndPoint;
            if (!IsDnsServer(remoteEndPoint))
            {
                _globalLogger.Warning("Request resolving (resolver {Resolver}). Response came from unexpected {Remote}. Response dropped",
                    _dnsEndpoint, remoteEndPoint);
                return;
            }

            var internalResponseId = buffer.DnsMessage.Header.Id;
            if (!_pendingRequests.TryGetValue(internalResponseId, out var responseCompletionSource))
            {
                _globalLogger.Warning("Request resolving (resolver {Resolver}). Unable to find response promise for request with internal Id: {Id}. Response dropped",
                    _dnsEndpoint, internalResponseId);
                return;
            }

            var messageHeader = buffer.DnsMessage.Header;
            messageHeader.Id = responseCompletionSource.OriginalRequestId;
            var response = Response.FromArray(buffer[..responseInfo.ReceivedBytes]);

            var logger = responseCompletionSource.Trace.Logger;
            logger.Debug("Got response for request with Id {IntId} -> {ExtId}",
                responseCompletionSource.InternalRequestId,
                responseCompletionSource.OriginalRequestId);

            if (responseCompletionSource.Request.Questions.Count != response.Questions.Count)
            {
                logger.Warning("Request resolving (req. {Id}). Questions count in response doesn't match request. Response dropped.\n{@Request}\n{@Response}",
                    internalResponseId,
                    responseCompletionSource.Request,
                    response);
                return;
            }

            if (!responseCompletionSource.Request.Questions.SequenceEqual(response.Questions, QuestionComparer.Instance))
            {
                logger.Warning("Request resolving (req. {Id}). Question doesn't match request. Response dropped.\n{@Request}\n{@Response}",
                    internalResponseId,
                    responseCompletionSource.Request,
                    response);
                return;
            }

            logger.Debug("Request resolving completed. Returning result ({Size} bytes)", responseInfo.ReceivedBytes);
            var completedEntry = new KeyValuePair<ushort, ResponsePromise>(internalResponseId, responseCompletionSource);
            _pendingRequests.TryRemove(completedEntry);
            responseCompletionSource.ResponseCompletionSource.TrySetResult(response);
        }
        finally
        {
            _arrayPool.Return(buffer);
        }
    }

    private bool IsDnsServer(IPEndPoint remoteEndPoint)
    {
        var remoteAddress = Normalize(remoteEndPoint.Address);
        return remoteEndPoint.Port == _dnsEndpoint.Port && remoteAddress.Equals(_dnsAddress);
    }

    // The socket is dual mode, so IPv4 addresses come mapped to IPv6
    private static IPAddress Normalize(IPAddress address)
    {
        return address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
    }

    private record ResponsePromise
    {
        public required ushort OriginalRequestId { get; init; }
        public required ushort InternalRequestId { get; init; }
        public required IRequest Request { get; init; }
        public required RequestTrace Trace { get; init; }
        public required TaskCompletionSource<IResponse> ResponseCompletionSource { get; init; }
    }
}
