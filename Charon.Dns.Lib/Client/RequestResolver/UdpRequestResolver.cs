#nullable enable
using System;
using System.Buffers;
using System.Linq;
using System.Net;
using System.Net.Sockets;
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
    
    private readonly TimeSpan _timeout = TimeSpan.FromSeconds(2);
    private readonly IPEndPoint _dnsEndpoint;
    private readonly ILogger _globalLogger;
    private readonly ArrayPool<byte> _arrayPool = ArrayPool<byte>.Shared;
    private readonly ResponsePromise?[] _sentRequestsBuffer;
    private ulong _internalRequestIdCounter;
    private readonly Socket _socket;
    private readonly CancellationTokenSource _resolvingCancellationToken;

    public UdpRequestResolver(
        IPEndPoint dnsEndpoint, 
        ByteUnit? socketBufferSize,
        ILogger globalLogger)
    {
        _dnsEndpoint = dnsEndpoint;
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
        
        var sentRequestsPoolSize = socketBufferSize?.Bytes / DefaultDnsMsgSize ?? 100;
        _sentRequestsBuffer = new ResponsePromise[Math.Min(sentRequestsPoolSize, ushort.MaxValue)];
        
        globalLogger.Information("UDP request resolver for DNS {DNS} created. Socket: {Socket}. UDP buffer: {UdpBuffer} bytes. Items buffer: {ItemsBuffer}",
            _dnsEndpoint,
            _socket.LocalEndPoint,
            socketBufferSizeBytes,
            _sentRequestsBuffer.Length);

        _ = Task.Run(async () =>
        {
            while (!_resolvingCancellationToken.IsCancellationRequested)
            {
                await HandleRequestFromSocket();
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
        
        var internalRequestId = (ushort)(Interlocked.Increment(ref _internalRequestIdCounter) % ushort.MaxValue);
        var bufferIndex = internalRequestId % _sentRequestsBuffer.Length;

        var originalRequestId = (ushort)request.Id;
        
        logger.Debug("Request resolving. Mapping request id {ExtId} -> {IntId}", originalRequestId, internalRequestId);
        
        var requestData = request.ToArray();
        var messageHeader = requestData.AsDnsMessage.Header; 
        messageHeader.Id = internalRequestId;
        
        await _socket.SendToAsync(
            requestData, 
            SocketFlags.None, 
            _dnsEndpoint,
            linkedCancellationToken);
        
        var taskCompletionSource = new TaskCompletionSource<IResponse>();
        
        await using var cancellationRegistration = linkedCancellationToken.Register(() => 
            taskCompletionSource.TrySetCanceled(linkedCancellationToken));
        
        var promise = new ResponsePromise
        {
            OriginalRequestId = originalRequestId,
            InternalRequestId = internalRequestId,
            Request = request,
            Trace = trace,
            ResponseCompletionSource = taskCompletionSource,
        };
        _sentRequestsBuffer[bufferIndex] = promise;

        try
        {
            return await taskCompletionSource.Task;
        }
        catch (Exception e)
        {
            logger.Error(e, "Request resolving error!. External id: {ExtId}; internal id: {IntId}", originalRequestId, internalRequestId);
            _sentRequestsBuffer[bufferIndex] = null;
            throw;
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

    private async Task HandleRequestFromSocket()
    {
        var cancellationToken = _resolvingCancellationToken.Token;
        var buffer = _arrayPool.Rent(DefaultDnsMsgSize * 8);
        try
        {
            var responseInfo = await _socket.ReceiveFromAsync(buffer, _dnsEndpoint, cancellationToken);
            var internalResponseId = buffer.AsDnsMessage.Header.Id;
            
            var bufferIndex = internalResponseId % _sentRequestsBuffer.Length;
            var responseCompletionSource = _sentRequestsBuffer[bufferIndex];

            if (responseCompletionSource is null)
            {
                _globalLogger.Warning("Request resolving (resolver {Resolver}). Unable to find response promise for request with internal Id: {Id}. Response dropped", 
                    _dnsEndpoint, internalResponseId);
                return;
            }

            var messageHeader = buffer.AsDnsMessage.Header; 
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
            _sentRequestsBuffer[bufferIndex] = null;
            responseCompletionSource.ResponseCompletionSource.TrySetResult(response);
        }
        finally
        {
            _arrayPool.Return(buffer);
        }
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
