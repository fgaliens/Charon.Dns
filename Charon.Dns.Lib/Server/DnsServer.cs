using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.IO;
using Charon.Dns.Lib.AsyncEvents;
using Charon.Dns.Lib.Client;
using Charon.Dns.Lib.Client.RequestResolver;
using Charon.Dns.Lib.Protocol;
using Charon.Dns.Lib.Protocol.Utils;

namespace Charon.Dns.Lib.Server
{
    public class DnsServer 
        : IAsyncObservable<OnRequestEventArgs>, 
            IAsyncObservable<OnResponseEventArgs>, 
            IAsyncObservable<OnExceptionEventArgs>, 
            IAsyncObservable<OnListeningEventArgs>, 
            IDisposable
    {
        private const int SioUdpConnreset = unchecked((int)0x9800000C);
        private const int DefaultPort = 53;
        private const int UdpTimeout = 2000;

        private bool _run = true;
        private bool _disposed;
        private UdpClient _udp;
        private readonly IRequestResolver _resolver;
        private readonly AsyncObservable<OnRequestEventArgs> _requestEventObservable = new();
        private readonly AsyncObservable<OnResponseEventArgs> _responseEventObservable = new();
        private readonly AsyncObservable<OnExceptionEventArgs> _exceptionEventObservable = new();
        private readonly AsyncObservable<OnListeningEventArgs> _listeningEventObservable = new();

        public DnsServer(IRequestResolver resolver, IPEndPoint endServer) :
            this(new FallbackRequestResolver(resolver, new UdpRequestResolver(endServer)))
        { }

        public DnsServer(IRequestResolver resolver, IPAddress endServer, int port = DefaultPort) :
            this(resolver, new IPEndPoint(endServer, port))
        { }

        public DnsServer(IRequestResolver resolver, string endServer, int port = DefaultPort) :
            this(resolver, IPAddress.Parse(endServer), port)
        { }

        public DnsServer(IPEndPoint endServer) :
            this(new UdpRequestResolver(endServer))
        { }

        public DnsServer(IPAddress endServer, int port = DefaultPort) :
            this(new IPEndPoint(endServer, port))
        { }

        public DnsServer(string endServer, int port = DefaultPort) :
            this(IPAddress.Parse(endServer), port)
        { }

        public DnsServer(IRequestResolver resolver)
        {
            _resolver = resolver;
        }

        public Task Listen(int port = DefaultPort, IPAddress ip = null)
        {
            return Listen(new IPEndPoint(ip ?? IPAddress.Any, port));
        }

        public async Task Listen(IPEndPoint endpoint)
        {
            await Task.Yield();

            TaskCompletionSource<object> tcs = new TaskCompletionSource<object>();

            if (_run)
            {
                try
                {
                    _udp = new UdpClient(endpoint);

                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {
                        _udp.Client.IOControl(SioUdpConnreset, new byte[4], new byte[4]);
                    }
                }
                catch (SocketException e)
                {
                    await OnError(e);
                    return;
                }
            }

            void ReceiveCallback(IAsyncResult result)
            {
                byte[] data;

                try
                {
                    IPEndPoint remote = new IPEndPoint(0, 0);
                    data = _udp.EndReceive(result, ref remote);
                    HandleRequest(data, remote);
                }
                catch (ObjectDisposedException)
                {
                    // run should already be false
                    _run = false;
                }
                catch (SocketException e)
                {
                    _ = OnError(e);
                }

                if (_run) _udp.BeginReceive(ReceiveCallback, null);
                else tcs.SetResult(null);
            }

            _udp.BeginReceive(ReceiveCallback, null);

            await _listeningEventObservable.SendEvent(new OnListeningEventArgs
            {
                Sender = this,
            });
            
            await tcs.Task.ConfigureAwait(false);
        }

        public void Dispose()
        {
            Dispose(true);
        }

        protected virtual void OnEvent<T>(EventHandler<T> handler, T args)
        {
            if (handler != null) handler(this, args);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                _disposed = true;

                if (disposing)
                {
                    _run = false;
                    _udp?.Dispose();
                }
            }
        }

        private async Task OnError(Exception e)
        {
            await _exceptionEventObservable.SendEvent(new OnExceptionEventArgs
            {
                Exception = e,
            });
        }

        private async void HandleRequest(byte[] data, IPEndPoint remote)
        {
            Request request = null;

            try
            {
                request = Request.FromArray(data);

                await _requestEventObservable.SendEvent(new OnRequestEventArgs
                {
                    Request = request,
                    Remote = remote,
                });

                IResponse response = await _resolver.Resolve(request).ConfigureAwait(false);

                await _responseEventObservable.SendEvent(new OnResponseEventArgs
                {
                    Request = request,
                    Response = response,
                    Remote = remote,
                });
                
                await _udp
                    .SendAsync(response.ToArray(), response.Size, remote)
                    .WithCancellationTimeout(TimeSpan.FromMilliseconds(UdpTimeout)).ConfigureAwait(false);
            }
            catch (SocketException e) { await OnError(e); }
            catch (ArgumentException e) { await OnError(e); }
            catch (IndexOutOfRangeException e) { await OnError(e); }
            catch (OperationCanceledException e) { await OnError(e); }
            catch (IOException e) { await OnError(e); }
            catch (ObjectDisposedException e) { await OnError(e); }
            catch (ResponseException e)
            {
                IResponse response = e.Response;

                if (response == null)
                {
                    response = Response.FromRequest(request);
                }

                try
                {
                    await _udp
                        .SendAsync(response.ToArray(), response.Size, remote)
                        .WithCancellationTimeout(TimeSpan.FromMilliseconds(UdpTimeout)).ConfigureAwait(false);
                }
                catch (SocketException) { }
                catch (OperationCanceledException) { }
                finally
                {
                    await _exceptionEventObservable.SendEvent(new OnExceptionEventArgs
                    {
                        Exception = e,
                    });
                }
            }
        }

        public class FallbackRequestResolver : IRequestResolver
        {
            private readonly IRequestResolver[] _resolvers;

            public FallbackRequestResolver(params IRequestResolver[] resolvers)
            {
                _resolvers = resolvers;
            }

            public async Task<IResponse> Resolve(IRequest request, CancellationToken cancellationToken = default(CancellationToken))
            {
                IResponse response = null;

                foreach (IRequestResolver resolver in _resolvers)
                {
                    response = await resolver.Resolve(request, cancellationToken).ConfigureAwait(false);
                    if (response.AnswerRecords.Count > 0) 
                        break;
                }

                return response;
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
