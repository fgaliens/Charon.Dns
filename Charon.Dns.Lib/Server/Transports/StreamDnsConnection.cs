using System;
using System.Buffers.Binary;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Charon.Dns.Lib.Protocol;
using Charon.Dns.Lib.Protocol.Utils;
using Serilog;

namespace Charon.Dns.Lib.Server.Transports;

/// <summary>
/// A TCP or TLS connection carrying DNS messages prefixed by their length (RFC 1035, 4.2.2; RFC 7858, 3.3).
/// </summary>
/// <remarks>
/// Queries are resolved concurrently and answered as soon as they are resolved,
/// so a slow query doesn't delay the following ones (RFC 7766, 6.2.1.1).
/// </remarks>
internal sealed class StreamDnsConnection(
    DnsServer server,
    Stream stream,
    IPEndPoint remote,
    bool padResponses,
    ILogger logger,
    CancellationToken cancellationToken)
{
    private const int LengthPrefixSize = 2;
    // Limits the resources a single client can hold (RFC 7766, 10)
    private const int MaxPendingRequests = 16;
    // RFC 7766, 6.2.3: idle connections are closed after a few seconds
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(10);

    private readonly SemaphoreSlim _pendingRequests = new(MaxPendingRequests, MaxPendingRequests);
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly CancellationTokenSource _connectionCancellation =
        CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    private readonly CancellationTokenSource _idleCancellation = new(IdleTimeout);
    private int _pendingCount;

    public async Task Run()
    {
        var connectionToken = _connectionCancellation.Token;
        using var readCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            connectionToken,
            _idleCancellation.Token);

        var lengthPrefix = new byte[LengthPrefixSize];

        try
        {
            while (true)
            {
                // Reading is paused while too many requests are pending
                await _pendingRequests.WaitAsync(connectionToken);

                byte[]? message;
                try
                {
                    message = await ReadMessage(lengthPrefix, readCancellation.Token);
                }
                catch
                {
                    _pendingRequests.Release();
                    throw;
                }

                if (message is null)
                {
                    _pendingRequests.Release();
                    break;
                }

                // The connection is not idle while a request is pending
                Interlocked.Increment(ref _pendingCount);
                _idleCancellation.CancelAfter(Timeout.InfiniteTimeSpan);
                _ = HandleMessage(message, connectionToken);
            }
        }
        catch (Exception e) when (e is OperationCanceledException or IOException)
        {
            logger.Debug("Dns server: connection with {Remote} closed: {Reason}", remote, e.Message);
        }
        finally
        {
            await WaitForPendingRequests();

            _connectionCancellation.Cancel();
            _idleCancellation.Dispose();
            _connectionCancellation.Dispose();
        }
    }

    /// <returns>Null if the client closed the connection or sent a malformed message.</returns>
    private async Task<byte[]?> ReadMessage(byte[] lengthPrefix, CancellationToken readToken)
    {
        var prefixLength = await stream.ReadAtLeastAsync(
            lengthPrefix,
            LengthPrefixSize,
            throwOnEndOfStream: false,
            readToken);
        if (prefixLength < LengthPrefixSize)
        {
            return null;
        }

        var messageLength = BinaryPrimitives.ReadUInt16BigEndian(lengthPrefix);
        if (messageLength < Header.HeaderSize)
        {
            // The framing can't be trusted anymore, so the connection is closed
            logger.Debug("Dns server: {Remote} sent a message of {Length} bytes. Connection closed", remote, messageLength);
            return null;
        }

        var message = new byte[messageLength];
        await stream.ReadExactlyAsync(message, readToken);
        return message;
    }

    private async Task HandleMessage(byte[] message, CancellationToken connectionToken)
    {
        try
        {
            var exchange = await server.ProcessMessage(message, remote, connectionToken);
            if (exchange is not null)
            {
                await WriteResponse(exchange, message, connectionToken);
            }
        }
        catch (Exception e)
        {
            if (!connectionToken.IsCancellationRequested)
            {
                logger.Warning(e, "Dns server: unable to answer {Remote}. Connection closed", remote);
            }

            // A failed write may leave a partial message in the stream
            await _connectionCancellation.CancelAsync();
        }
        finally
        {
            // Done before the release: the connection is disposed as soon as all requests are released
            if (Interlocked.Decrement(ref _pendingCount) == 0)
            {
                // The last pending request is answered, the connection is idle
                _idleCancellation.CancelAfter(IdleTimeout);
            }

            _pendingRequests.Release();
        }
    }

    private async Task WriteResponse(DnsExchange exchange, byte[] message, CancellationToken connectionToken)
    {
        var (request, response, trace) = exchange;
        var pad = padResponses && EdnsPadding.IsRequested(request);
        byte[] buffer;
        int size;

        try
        {
            buffer = ResponseSerializer.Rent(response, pad, LengthPrefixSize, out size);
        }
        catch (Exception e)
        {
            trace.Logger.Error(e, "Dns server error. Unable to serialize response");
            await server.OnError(e, trace);

            var errorResponse = server.CreateErrorResponse(request, message);
            if (errorResponse is null)
            {
                return;
            }

            buffer = ResponseSerializer.Rent(errorResponse, pad, LengthPrefixSize, out size);
        }

        try
        {
            BinaryPrimitives.WriteUInt16BigEndian(buffer, (ushort)size);
            var frame = buffer.AsMemory(0, LengthPrefixSize + size);

            // RFC 7766, 8: the length and the message are sent in a single write
            await _writeLock.WaitAsync(connectionToken);
            try
            {
                await stream.WriteAsync(frame, connectionToken);
                await stream.FlushAsync(connectionToken);
            }
            finally
            {
                _writeLock.Release();
            }

            trace.Logger.Debug("Dns server: response sent to {Remote}", remote);
        }
        finally
        {
            ResponseSerializer.Return(buffer);
        }
    }

    /// <summary>
    /// Responses are written to the stream, so it is disposed only after all of them are sent.
    /// </summary>
    private async Task WaitForPendingRequests()
    {
        for (var i = 0; i < MaxPendingRequests; i++)
        {
            await _pendingRequests.WaitAsync();
        }
    }
}
