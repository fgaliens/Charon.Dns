using Charon.Dns.Lib.AsyncEvents;
using Charon.Dns.Lib.Protocol;
using Charon.Dns.Lib.Tracing;

namespace Charon.Dns.Interceptors
{
    public interface IRequestInterceptor : IAsyncObserver<OnRequestEventArgs>
    {
        Task Handle(
            IReadOnlyRequest request,
            RequestTrace trace,
            CancellationToken token = default);
    }
}
