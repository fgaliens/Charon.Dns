using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
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
using Serilog;

namespace Charon.Dns.Lib.Client.RequestResolver;

/// <summary>
/// A client sending DNS messages prefixed by their length (RFC 1035, 4.2.2) over a TCP or TLS connection.
/// </summary>
/// <remarks>
/// One connection is kept open and reused for all queries, which are pipelined without waiting
/// for previous responses (RFC 7766, 6.2.1; RFC 7858, 3.4). Responses may come out of order,
/// so they are matched by an internal id and the question (RFC 7766, 6.2.1.1).
/// </remarks>
public abstract class StreamRequestResolver : IRequestResolver, IDisposable
{
    private const int LengthPrefixSize = 2;
    private const int MaxRequestIdAttempts = 32;
    // RFC 7766, 6.2.2: a query without a response is retried once on a new connection,
    // as the server may close an idle connection at any moment
    private const int MaxAttempts = 2;
    // RFC 7766, 6.2.2: the client closes an idle connection. It is longer than the query timeout,
    // so a connection is never closed as idle while a query is pending
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(20);

    private readonly TimeSpan _timeout;
    private readonly SemaphoreSlim _connectLock = new(1, 1);
    private readonly CancellationTokenSource _resolvingCancellation = new();
    private Connection? _connection;

    protected StreamRequestResolver(IPEndPoint dnsEndpoint, TimeSpan timeout, ILogger logger)
    {
        DnsEndpoint = dnsEndpoint;
        Logger = logger;
        _timeout = timeout;
    }

    protected IPEndPoint DnsEndpoint { get; }

    protected ILogger Logger { get; }

    protected abstract string Transport { get; }

    public async Task<IResponse> Resolve(IRequest request, RequestTrace trace, CancellationToken cancellationToken = default)
    {
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            _resolvingCancellation.Token,
            cancellationToken);
        linkedCancellation.CancelAfter(_timeout);
        var linkedToken = linkedCancellation.Token;

        var wireRequest = PrepareRequest(request);

        try
        {
            for (var attempt = 1;; attempt++)
            {
                var connection = await GetConnection(linkedToken);
                try
                {
                    return await connection.Send(request, wireRequest, trace, linkedToken);
                }
                catch (ConnectionClosedException e) when (attempt < MaxAttempts && !linkedToken.IsCancellationRequested)
                {
                    trace.Logger.Debug("Request resolving. {Transport} connection to {DNS} closed: {Reason}. Retrying",
                        Transport, DnsEndpoint, e.Message);
                }
            }
        }
        catch (Exception e)
        {
            trace.Logger.Error(e, "Request resolving error! {Transport} DNS {DNS}, request id: {Id}", Transport, DnsEndpoint, request.Id);
            throw;
        }
    }

    public void Dispose()
    {
        if (_resolvingCancellation.IsCancellationRequested)
        {
            return;
        }

        _resolvingCancellation.Cancel();
        _connection?.Close(null);
    }

    /// <summary>
    /// Opens a connection to <see cref="DnsEndpoint"/>; the stream owns the socket.
    /// </summary>
    protected abstract Task<Stream> Connect(CancellationToken cancellationToken);

    /// <summary>
    /// The request as it is sent to the server, the response is still matched against the original one.
    /// </summary>
    protected virtual IRequest PrepareRequest(IRequest request)
    {
        return request;
    }

    protected async Task<Socket> ConnectSocket(CancellationToken cancellationToken)
    {
        var socket = new Socket(DnsEndpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            socket.NoDelay = true;
            await socket.ConnectAsync(DnsEndpoint, cancellationToken);
        }
        catch
        {
            socket.Dispose();
            throw;
        }

        return socket;
    }

    private async Task<Connection> GetConnection(CancellationToken cancellationToken)
    {
        var connection = _connection;
        if (connection is { IsClosed: false })
        {
            return connection;
        }

        await _connectLock.WaitAsync(cancellationToken);
        try
        {
            // Another query may have connected while this one was waiting
            connection = _connection;
            if (connection is { IsClosed: false })
            {
                return connection;
            }

            var stream = await Connect(cancellationToken);
            connection = new Connection(this, stream);
            _connection = connection;

            Logger.Debug("Request resolving. {Transport} connection to {DNS} opened", Transport, DnsEndpoint);
            return connection;
        }
        finally
        {
            _connectLock.Release();
        }
    }

    /// <summary>
    /// The connection is gone before the response came, the query can be retried on a new one.
    /// </summary>
    private sealed class ConnectionClosedException(string message, Exception? innerException)
        : IOException(message, innerException);

    private sealed class Connection
    {
        private readonly StreamRequestResolver _resolver;
        private readonly Stream _stream;
        private readonly ConcurrentDictionary<ushort, ResponsePromise> _pendingRequests = new();
        private readonly SemaphoreSlim _writeLock = new(1, 1);
        private readonly CancellationTokenSource _closeCancellation = new();
        private readonly CancellationTokenSource _idleCancellation = new(IdleTimeout);
        private int _pendingCount;
        private int _isClosed;

        public Connection(StreamRequestResolver resolver, Stream stream)
        {
            _resolver = resolver;
            _stream = stream;
            _ = Task.Run(ReadResponses);
        }

        public bool IsClosed => Volatile.Read(ref _isClosed) != 0;

        private ILogger Logger => _resolver.Logger;

        public async Task<IResponse> Send(
            IRequest request,
            IRequest wireRequest,
            RequestTrace trace,
            CancellationToken cancellationToken)
        {
            var promise = RegisterPromise(request, trace);
            var internalRequestId = promise.InternalRequestId;
            trace.Logger.Debug("Request resolving. Mapping request id {ExtId} -> {IntId}", promise.OriginalRequestId, internalRequestId);

            try
            {
                await using var cancellationRegistration = cancellationToken.Register(() =>
                    promise.ResponseCompletionSource.TrySetCanceled(cancellationToken));

                // Checked after the registration: a connection closed earlier doesn't fail the promise
                if (IsClosed)
                {
                    throw new ConnectionClosedException("Connection is closed", null);
                }

                await Write(wireRequest, internalRequestId, cancellationToken);

                return await promise.ResponseCompletionSource.Task;
            }
            finally
            {
                var ownEntry = new KeyValuePair<ushort, ResponsePromise>(internalRequestId, promise);
                _pendingRequests.TryRemove(ownEntry);

                if (Interlocked.Decrement(ref _pendingCount) == 0)
                {
                    _idleCancellation.CancelAfter(IdleTimeout);
                }
            }
        }

        public void Close(Exception? error)
        {
            if (Interlocked.Exchange(ref _isClosed, 1) != 0)
            {
                return;
            }

            _closeCancellation.Cancel();
            _idleCancellation.Cancel();

            foreach (var promise in _pendingRequests.Values)
            {
                var exception = new ConnectionClosedException(error?.Message ?? "Connection is closed", error);
                promise.ResponseCompletionSource.TrySetException(exception);
            }

            _stream.Dispose();
        }

        /// <summary>
        /// A random id makes it hard to spoof a response, and ids of different clients never collide.
        /// </summary>
        private ResponsePromise RegisterPromise(IRequest request, RequestTrace trace)
        {
            // The connection is not idle while a request is pending
            Interlocked.Increment(ref _pendingCount);
            _idleCancellation.CancelAfter(Timeout.InfiniteTimeSpan);

            for (var i = 0; i < MaxRequestIdAttempts; i++)
            {
                var internalRequestId = (ushort)RandomNumberGenerator.GetInt32(ushort.MaxValue + 1);
                var promise = new ResponsePromise
                {
                    OriginalRequestId = (ushort)request.Id,
                    InternalRequestId = internalRequestId,
                    Request = request,
                    Trace = trace,
                    ResponseCompletionSource = new TaskCompletionSource<IResponse>(
                        TaskCreationOptions.RunContinuationsAsynchronously),
                };

                if (_pendingRequests.TryAdd(internalRequestId, promise))
                {
                    return promise;
                }
            }

            Interlocked.Decrement(ref _pendingCount);
            throw new InvalidOperationException($"Unable to allocate request id, {_pendingRequests.Count} requests are pending");
        }

        private async Task Write(IRequest wireRequest, ushort internalRequestId, CancellationToken cancellationToken)
        {
            var requestSize = wireRequest.Size;
            var frameSize = LengthPrefixSize + requestSize;
            var buffer = ArrayPool<byte>.Shared.Rent(frameSize);

            try
            {
                BinaryPrimitives.WriteUInt16BigEndian(buffer, (ushort)requestSize);
                var message = buffer.AsMemory(LengthPrefixSize, requestSize);
                wireRequest.WriteTo(message.Span);
                var messageHeader = message.DnsMessage.Header;
                messageHeader.Id = internalRequestId;

                await _writeLock.WaitAsync(cancellationToken);
                try
                {
                    // RFC 7766, 8: the length and the message are sent in a single write
                    var frame = buffer.AsMemory(0, frameSize);
                    await _stream.WriteAsync(frame, cancellationToken);
                    await _stream.FlushAsync(cancellationToken);
                }
                catch (Exception e)
                {
                    // A partially written message breaks the framing of the following ones
                    Close(e);
                    throw new ConnectionClosedException($"Unable to send request: {e.Message}", e);
                }
                finally
                {
                    _writeLock.Release();
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        private async Task ReadResponses()
        {
            using var readCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                _closeCancellation.Token,
                _idleCancellation.Token);
            var readToken = readCancellation.Token;
            var lengthPrefix = new byte[LengthPrefixSize];
            Exception? error = null;

            try
            {
                while (true)
                {
                    var prefixLength = await _stream.ReadAtLeastAsync(
                        lengthPrefix,
                        LengthPrefixSize,
                        throwOnEndOfStream: false,
                        readToken);
                    if (prefixLength < LengthPrefixSize)
                    {
                        Logger.Debug("Request resolving. {Transport} connection to {DNS} closed by the server",
                            _resolver.Transport, _resolver.DnsEndpoint);
                        break;
                    }

                    var messageLength = BinaryPrimitives.ReadUInt16BigEndian(lengthPrefix);
                    var message = new byte[messageLength];
                    await _stream.ReadExactlyAsync(message, readToken);

                    HandleResponse(message);
                }
            }
            catch (Exception e)
            {
                if (!IsClosed && !_idleCancellation.IsCancellationRequested)
                {
                    Logger.Debug("Request resolving. {Transport} connection to {DNS} failed: {Reason}",
                        _resolver.Transport, _resolver.DnsEndpoint, e.Message);
                }

                error = e;
            }
            finally
            {
                Close(error);
            }
        }

        private void HandleResponse(byte[] message)
        {
            var dns = _resolver.DnsEndpoint;
            if (message.Length < Header.HeaderSize)
            {
                Logger.Warning("Request resolving (resolver {Resolver}). Response of {Size} bytes is too short. Response dropped",
                    dns, message.Length);
                return;
            }

            var messageHeader = message.DnsMessage.Header;
            var internalResponseId = messageHeader.Id;
            if (!_pendingRequests.TryRemove(internalResponseId, out var promise))
            {
                // The query has been cancelled already
                Logger.Debug("Request resolving (resolver {Resolver}). Unable to find response promise for request with internal Id: {Id}. Response dropped",
                    dns, internalResponseId);
                return;
            }

            var logger = promise.Trace.Logger;
            messageHeader.Id = promise.OriginalRequestId;

            Response response;
            try
            {
                response = Response.FromArray(message);
            }
            catch (Exception e)
            {
                promise.ResponseCompletionSource.TrySetException(e);
                return;
            }

            logger.Debug("Got response for request with Id {IntId} -> {ExtId}", internalResponseId, promise.OriginalRequestId);

            if (!promise.Request.Questions.SequenceEqual(response.Questions, QuestionComparer.Instance))
            {
                logger.Warning("Request resolving (req. {Id}). Question doesn't match request.\n{@Request}\n{@Response}",
                    internalResponseId,
                    promise.Request,
                    response);
                var exception = new ResponseException(response, "Question in response doesn't match request");
                promise.ResponseCompletionSource.TrySetException(exception);
                return;
            }

            logger.Debug("Request resolving completed. Returning result ({Size} bytes)", message.Length);
            promise.ResponseCompletionSource.TrySetResult(response);
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
