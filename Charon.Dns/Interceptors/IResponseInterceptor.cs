using Charon.Dns.Lib.AsyncEvents;
using Charon.Dns.Lib.Protocol;
using Charon.Dns.Lib.Tracing;

namespace Charon.Dns.Interceptors
{
    public interface IResponseInterceptor : IAsyncObserver<OnResponseEventArgs>
    {
        Task Handle(
            IReadOnlyRequest request,
            IReadOnlyResponse response,
            RequestTrace trace,
            CancellationToken token = default);
    }

}
