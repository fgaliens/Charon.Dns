#nullable enable
using System;
using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Charon.Dns.Lib.AsyncEvents;
using Charon.Dns.Lib.Client.RequestResolver;
using Charon.Dns.Lib.Protocol;
using Charon.Dns.Lib.Tracing;
using Serilog;

namespace Charon.Dns.Lib.Server
{
    public class DnsServer(
        IRequestResolver resolver,
        IRequestCounter requestCounter,
        ILogger logger)
            : IAsyncObservable<OnRequestEventArgs>,
            IAsyncObservable<OnResponseEventArgs>,
            IAsyncObservable<OnExceptionEventArgs>,
            IAsyncObservable<OnListeningEventArgs>
    {
        private static readonly ArrayPool<byte> ArrayPool = ArrayPool<byte>.Shared;

        private const int MaxUdpRequestSize = 4096;

        private readonly AsyncObservable<OnRequestEventArgs> _requestEventObservable = new();
        private readonly AsyncObservable<OnResponseEventArgs> _responseEventObservable = new();
        private readonly AsyncObservable<OnExceptionEventArgs> _exceptionEventObservable = new();
        private readonly AsyncObservable<OnListeningEventArgs> _listeningEventObservable = new();

        public async Task Listen(IPEndPoint endpoint, CancellationToken cancellationToken = default)
        {
            using var udpSocket = new Socket(SocketType.Dgram, ProtocolType.Udp);
            udpSocket.ReceiveBufferSize = 4 * 1024 * 1024;
            udpSocket.SendBufferSize = 4 * 1024 * 1024;
            udpSocket.Bind(endpoint);

            while (!cancellationToken.IsCancellationRequested)
            {
                var buffer = ArrayPool.Rent(MaxUdpRequestSize * 2);
                var requestInfo = await udpSocket.ReceiveFromAsync(buffer, endpoint, cancellationToken);
                _ = HandleRequest(udpSocket, buffer, requestInfo, cancellationToken);
            }
        }

        private async Task OnError(Exception e, RequestTrace? trace)
        {
            await _exceptionEventObservable.SendEvent(new OnExceptionEventArgs
            {
                Exception = e,
                Trace = trace,
            });
        }

        private async Task HandleRequest(
            Socket socket, 
            byte[] buffer, 
            SocketReceiveFromResult dataInfo,
            CancellationToken cancellationToken)
        {
            await Task.Yield();

            var requestId = requestCounter.Increment();

            var requestLogger = logger.ForContext("RequestId", requestId);
            
            var message = buffer[..dataInfo.ReceivedBytes];
            var remote = (IPEndPoint)dataInfo.RemoteEndPoint;
            var trace = new RequestTrace
            {
                Id = requestId,
                RemoteEndPoint = remote,
                Logger = requestLogger,
            };
            
            requestLogger.Debug("Dns server: handling request from {Remote}", remote);

            Request? request = null;

            try
            {
                request = Request.FromArray(message);

                await _requestEventObservable.SendEvent(new OnRequestEventArgs
                {
                    Request = request,
                    Trace = trace,
                });

                IResponse response = await resolver.Resolve(request, trace, cancellationToken);

                requestLogger.Debug("Dns server: got response from resolver");
                    
                await _responseEventObservable.SendEvent(new OnResponseEventArgs
                {
                    Request = request,
                    Response = response,
                    Trace = trace,
                });
                
                requestLogger.Debug("Dns server: sending response to {Remote}", remote);

                await socket.SendToAsync(response.ToArray(), SocketFlags.None, remote, cancellationToken);
                
                requestLogger.Debug("Dns server: response to {Remote} sent", remote);
            }
            catch (Exception e)
            {
                requestLogger.Error(e, "Dns server error");
                
                await OnError(e, trace);

                try
                {
                    var response = Response.FromRequest(request);
                    response.ResponseCode = ResponseCode.ServerFailure;
                    await socket.SendToAsync(response.ToArray(), SocketFlags.None, remote, cancellationToken);
                }
                catch (Exception sendErrorException)
                {
                    var aggregatedException = new AggregateException(e, sendErrorException);
                    requestLogger.Fatal(aggregatedException, "Dns server fatal error. Unable to send response");
                    
                    await OnError(sendErrorException, trace);
                }
            }
            finally
            {
                ArrayPool.Return(buffer, clearArray: true);
            }
        }

        public class FallbackRequestResolver : IRequestResolver
        {
            private readonly IRequestResolver[] _resolvers;

            public FallbackRequestResolver(params IRequestResolver[] resolvers)
            {
                _resolvers = resolvers;
            }

            public async Task<IResponse> Resolve(
                IRequest request, 
                RequestTrace trace, 
                CancellationToken cancellationToken = default)
            {
                IResponse? response = null;

                foreach (var resolver in _resolvers)
                {
                    response = await resolver.Resolve(request, trace, cancellationToken);
                    if (response.AnswerRecords.Count > 0)
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
}
