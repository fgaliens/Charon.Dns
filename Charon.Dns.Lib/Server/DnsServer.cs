#nullable enable
using System;
using System.Buffers;
using System.Collections.Generic;
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
        int parallelizationFactor,
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
            var tasks = new List<Task>(parallelizationFactor);
            for (int i = 0; i < parallelizationFactor; i++)
            {
                int socketIndex = i;
                var task = Task.Run(async () =>
                {
                    using var socket = new Socket(SocketType.Dgram, ProtocolType.Udp);
                    socket.ReceiveBufferSize = 1 * 1024 * 1024;
                    socket.ExclusiveAddressUse = false;
                    socket.Bind(endpoint);

                    while (!cancellationToken.IsCancellationRequested)
                    {
                        var buffer = ArrayPool.Rent(MaxUdpRequestSize * 2);
                        var requestInfo = await socket.ReceiveFromAsync(buffer, endpoint, cancellationToken);
                        await HandleRequest(socket, socketIndex, buffer, requestInfo, cancellationToken);
                    }
                }, cancellationToken);
                tasks.Add(task);
            }

            await Task.WhenAll(tasks);
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
            int socketIndex,
            byte[] buffer,
            SocketReceiveFromResult dataInfo,
            CancellationToken cancellationToken)
        {
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
            
            requestLogger.Debug("Dns server (socket #{SocketIndex}): handling request from {Remote}", socketIndex, remote);

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

                requestLogger.Debug("Dns server (socket #{SocketIndex}): got response from resolver", socketIndex);
                    
                await _responseEventObservable.SendEvent(new OnResponseEventArgs
                {
                    Request = request,
                    Response = response,
                    Trace = trace,
                });
                
                requestLogger.Debug("Dns server (socket #{SocketIndex}): sending response to {Remote}", socketIndex, remote);

                await socket.SendToAsync(response.ToArray(), SocketFlags.None, remote, cancellationToken);
                
                requestLogger.Debug("Dns server (socket #{SocketIndex}): response to {Remote} sent", socketIndex, remote);
            }
            catch (Exception e)
            {
                requestLogger.Error(e, "Dns server error (socket #{SocketIndex})", socketIndex);
                
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
                    requestLogger.Fatal(aggregatedException, "Dns server fatal error. Unable to send response (socket #{SocketIndex})", socketIndex);
                    
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
