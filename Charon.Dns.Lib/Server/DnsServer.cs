using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Charon.Dns.Lib.AsyncEvents;
using Charon.Dns.Lib.Client.RequestResolver;
using Charon.Dns.Lib.Protocol;
using Charon.Dns.Lib.Protocol.ResourceRecords;
using Charon.Dns.Lib.Server.Transports;
using Charon.Dns.Lib.Tracing;
using Charon.Dns.Utils.Units;
using Serilog;
using Serilog.Context;

namespace Charon.Dns.Lib.Server;

public class DnsServer(
    IRequestResolver resolver,
    IRequestCounter requestCounter,
    int parallelizationFactor,
    ByteUnit socketBufferSize,
    ILogger logger,
    bool recursionAvailable = false)
        : IAsyncObservable<OnRequestEventArgs>,
        IAsyncObservable<OnResponseEventArgs>,
        IAsyncObservable<OnExceptionEventArgs>,
        IAsyncObservable<OnListeningEventArgs>
{
    private static readonly ArrayPool<byte> ArrayPool = ArrayPool<byte>.Shared;

    private const int MaxUdpRequestSize = 4096;
    // RFC 1035, 4.2.1: without EDNS a UDP message is limited to 512 bytes
    private const int MinUdpPayloadSize = 512;
    // Recommended by DNS Flag Day 2020 to avoid IP fragmentation
    internal const RecordClass AdvertisedUdpPayloadSize = (RecordClass)1232;
    internal static readonly Domain RootName = new(string.Empty);

    private readonly AsyncObservable<OnRequestEventArgs> _requestEventObservable = new();
    private readonly AsyncObservable<OnResponseEventArgs> _responseEventObservable = new();
    private readonly AsyncObservable<OnExceptionEventArgs> _exceptionEventObservable = new();
    private readonly AsyncObservable<OnListeningEventArgs> _listeningEventObservable = new();

    public async Task Listen(IPEndPoint endpoint, bool enableIpV6, CancellationToken cancellationToken = default)
    {
        var tasks = new List<Task>(parallelizationFactor);
        for (var i = 0; i < parallelizationFactor; i++)
        {
            var task = Task.Run(async () =>
            {
                var addressFamily = enableIpV6 ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork;
                using var socket = CreateSocket(addressFamily, endpoint);

                while (!cancellationToken.IsCancellationRequested)
                {
                    await HandleRequest(socket, endpoint, cancellationToken);
                }
            }, cancellationToken);
            tasks.Add(task);
        }

        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// DNS over TCP (RFC 7766). It is mandatory for DNS servers (RFC 9210), so it is served next to UDP.
    /// </summary>
    public Task ListenTcp(IPEndPoint endpoint, bool enableIpV6, CancellationToken cancellationToken = default)
    {
        var listener = new StreamDnsListener(this, certificate: null, logger);
        return listener.Listen(endpoint, enableIpV6, cancellationToken);
    }

    /// <summary>
    /// DNS over TLS (RFC 7858).
    /// </summary>
    public Task ListenTls(
        IPEndPoint endpoint,
        bool enableIpV6,
        X509Certificate2 certificate,
        CancellationToken cancellationToken = default)
    {
        var listener = new StreamDnsListener(this, certificate, logger);
        return listener.Listen(endpoint, enableIpV6, cancellationToken);
    }

    /// <summary>
    /// DNS over HTTPS (RFC 8484), served on <paramref name="path"/>.
    /// </summary>
    public Task ListenHttps(
        IPEndPoint endpoint,
        bool enableIpV6,
        X509Certificate2 certificate,
        string path,
        CancellationToken cancellationToken = default)
    {
        var listener = new HttpsDnsListener(this, certificate, path, logger);
        return listener.Listen(endpoint, enableIpV6, cancellationToken);
    }

    internal async Task OnError(Exception e, RequestTrace? trace)
    {
        await _exceptionEventObservable.SendEvent(new OnExceptionEventArgs
        {
            Exception = e,
            Trace = trace,
        });
    }

    /// <summary>
    /// Resolves a request received by any transport, so all of them share events and error handling.
    /// </summary>
    /// <returns>Null if the message must not be answered.</returns>
    internal async Task<DnsExchange?> ProcessMessage(
        byte[] message,
        IPEndPoint remote,
        CancellationToken cancellationToken)
    {
        var requestId = requestCounter.Increment();
        var requestLogger = logger.ForContext("RequestId", requestId);

        using var requestIdLogContext = LogContext.PushProperty("RequestId", requestId);

        var trace = new RequestTrace
        {
            Id = requestId,
            RemoteEndPoint = remote,
            Logger = requestLogger,
        };
        Request? request = null;

        try
        {
            requestLogger.Debug("Dns server: handling request from {Remote}", remote);

            request = Request.FromArray(message);

            await _requestEventObservable.SendEvent(new OnRequestEventArgs
            {
                Request = request,
                Trace = trace,
            });

            var response = await resolver.Resolve(request, trace, cancellationToken);

            requestLogger.Debug("Dns server: got response from resolver");

            await _responseEventObservable.SendEvent(new OnResponseEventArgs
            {
                Request = request,
                Response = response,
            });

            return new DnsExchange(request, response, trace);
        }
        catch (Exception e)
        {
            requestLogger.Error(e, "Dns server error");

            await OnError(e, trace);

            var errorResponse = CreateErrorResponse(request, message);
            return errorResponse is null ? null : new DnsExchange(request, errorResponse, trace);
        }
    }

    private async Task HandleRequest(Socket socket, IPEndPoint endpoint, CancellationToken cancellationToken)
    {
        var buffer = ArrayPool.Rent(MaxUdpRequestSize * 2);

        try
        {
            var requestInfo = await socket.ReceiveFromAsync(buffer, endpoint, cancellationToken);
            var message = buffer[..requestInfo.ReceivedBytes];
            var remote = (IPEndPoint)requestInfo.RemoteEndPoint;

            var exchange = await ProcessMessage(message, remote, cancellationToken);
            if (exchange is null)
            {
                return;
            }

            var (request, response, trace) = exchange;
            var requestLogger = trace.Logger;

            try
            {
                requestLogger.Debug("Dns server: sending response to {Remote}", remote);

                await SendResponse(socket, response, request, remote, cancellationToken);

                requestLogger.Debug("Dns server: response sent to {Remote}", remote);
            }
            catch (Exception e)
            {
                requestLogger.Error(e, "Dns server error. Unable to send response");

                await OnError(e, trace);

                await TrySendErrorResponse(socket, request, message, remote, trace, e, cancellationToken);
            }
        }
        finally
        {
            ArrayPool.Return(buffer, clearArray: true);
        }
    }

    /// <summary>
    /// A response that can't be serialized is replaced by an error, so the client doesn't wait for a timeout.
    /// </summary>
    private async Task TrySendErrorResponse(
        Socket socket,
        Request? request,
        byte[] message,
        IPEndPoint remote,
        RequestTrace trace,
        Exception exception,
        CancellationToken cancellationToken)
    {
        try
        {
            var errorResponse = CreateErrorResponse(request, message);
            if (errorResponse is not null)
            {
                await SendResponse(socket, errorResponse, request, remote, cancellationToken);
            }
        }
        catch (Exception sendErrorException)
        {
            var aggregatedException = new AggregateException(exception, sendErrorException);
            trace.Logger.Fatal(aggregatedException, "Dns server fatal error. Unable to send response");

            await OnError(sendErrorException, trace);
        }
    }

    internal Response? CreateErrorResponse(Request? request, byte[]? message)
    {
        if (request is not null)
        {
            var serverFailure = Response.FromRequest(request);
            serverFailure.ResponseCode = ResponseCode.ServerFailure;
            serverFailure.RecursionAvailable = recursionAvailable;

            // RFC 6891, 7: a response to an EDNS request carries an OPT record
            if (request.AdditionalRecords.Any(record => record.Type == RecordType.OPT))
            {
                var opt = new ResourceRecord(RootName, [], RecordType.OPT, AdvertisedUdpPayloadSize);
                serverFailure.AdditionalRecords.Add(opt);
            }

            return serverFailure;
        }

        if (message is null || message.Length < Header.HeaderSize)
        {
            return null;
        }

        // A response is never answered, otherwise two servers could answer each other endlessly
        var header = Header.FromArray(message);
        if (header.Response)
        {
            return null;
        }

        return new Response
        {
            Id = header.Id,
            OperationCode = header.OperationCode,
            RecursionDesired = header.RecursionDesired,
            RecursionAvailable = recursionAvailable,
            ResponseCode = ResponseCode.FormatError,
        };
    }

    private static async Task SendResponse(
        Socket socket,
        IResponse response,
        IReadOnlyRequest? request,
        IPEndPoint remote,
        CancellationToken cancellationToken)
    {
        var maxResponseSize = GetMaxResponseSize(request);
        var responseSize = response.Size;
        if (responseSize > maxResponseSize)
        {
            response = Truncate(response);
            responseSize = response.Size;
        }

        var responseBuffer = ArrayPool.Rent(responseSize);

        try
        {
            response.WriteTo(responseBuffer.AsSpan(0, responseSize));
            await socket.SendToAsync(responseBuffer.AsMemory(0, responseSize), SocketFlags.None, remote, cancellationToken);
        }
        finally
        {
            ArrayPool.Return(responseBuffer, clearArray: true);
        }
    }

    /// <summary>
    /// RFC 6891, 6.2.5: a payload size less than 512 is treated as 512.
    /// </summary>
    internal static int GetMaxResponseSize(IReadOnlyRequest? request)
    {
        if (request is null)
        {
            return MinUdpPayloadSize;
        }

        foreach (var record in request.AdditionalRecords)
        {
            if (record.Type == RecordType.OPT)
            {
                int payloadSize = (ushort)record.Class;
                return Math.Max(MinUdpPayloadSize, payloadSize);
            }
        }

        return MinUdpPayloadSize;
    }

    /// <summary>
    /// RFC 2181, 9: records are dropped and the TC flag is set, so the client may retry over TCP.
    /// The OPT record is kept, as it describes the server rather than the answer.
    /// </summary>
    internal static Response Truncate(IReadOnlyResponse response)
    {
        var truncated = new Response(response)
        {
            Truncated = true,
        };
        truncated.AnswerRecords.Clear();
        truncated.AuthorityRecords.Clear();

        var additionalRecords = truncated.AdditionalRecords;
        for (var i = additionalRecords.Count - 1; i >= 0; i--)
        {
            if (additionalRecords[i].Type != RecordType.OPT)
            {
                additionalRecords.RemoveAt(i);
            }
        }

        return truncated;
    }

    private Socket CreateSocket(AddressFamily addressFamily, IPEndPoint endpoint)
    {
        var socket = new Socket(addressFamily, SocketType.Dgram, ProtocolType.Udp);
        socket.ReceiveBufferSize = socketBufferSize.Bytes;
        socket.ExclusiveAddressUse = false;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // ExclusiveAddressUse = false only grants SO_REUSEADDR: multiple sockets can bind
            // the same address/port, but the kernel still funnels every unicast datagram to
            // just one of them. SO_REUSEPORT (option 15) makes Linux hash-distribute traffic
            // across all of them instead; macOS/BSD's SO_REUSEPORT doesn't do this for UDP.
            socket.SetRawSocketOption(1, 15, BitConverter.GetBytes(1));
        }

        socket.Bind(endpoint);
        return socket;
    }

    public class FallbackRequestResolver(params IRequestResolver[] resolvers) : IRequestResolver
    {
        public async Task<IResponse> Resolve(
            IRequest request,
            RequestTrace trace,
            CancellationToken cancellationToken = default)
        {
            IResponse? response = null;

            foreach (var resolver in resolvers)
            {
                response = await resolver.Resolve(request, trace, cancellationToken);
                // Authoritative response is final even without answers (NXDOMAIN / NODATA)
                if (response.AnswerRecords.Count > 0 || response.AuthorativeServer)
                {
                    break;
                }
            }

            return response!;
        }
    }

    public IAsyncDisposable Subscribe(IAsyncObserver<OnResponseEventArgs> observer)
    {
        return _responseEventObservable.Subscribe(observer);
    }

    public IAsyncDisposable Subscribe(IAsyncObserver<OnRequestEventArgs> observer)
    {
        return _requestEventObservable.Subscribe(observer);
    }

    public IAsyncDisposable Subscribe(IAsyncObserver<OnExceptionEventArgs> observer)
    {
        return _exceptionEventObservable.Subscribe(observer);
    }

    public IAsyncDisposable Subscribe(IAsyncObserver<OnListeningEventArgs> observer)
    {
        return _listeningEventObservable.Subscribe(observer);
    }
}

internal sealed record DnsExchange(Request? Request, IResponse Response, RequestTrace Trace);
