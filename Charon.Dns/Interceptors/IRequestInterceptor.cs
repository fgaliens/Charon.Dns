using System.Net;
using Charon.Dns.Lib.AsyncEvents;
using Charon.Dns.Lib.Protocol;

namespace Charon.Dns.Interceptors
{
    public interface IRequestInterceptor : IAsyncObserver<OnRequestEventArgs>
    {
        Task Handle(
            IReadOnlyRequest request,
            IPEndPoint remoteEndPoint,
            CancellationToken token = default);
    }
}
