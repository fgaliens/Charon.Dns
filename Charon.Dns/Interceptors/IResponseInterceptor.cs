using Charon.Dns.Lib.AsyncEvents;
using Charon.Dns.Lib.Protocol;

namespace Charon.Dns.Interceptors
{
    public interface IResponseInterceptor : IAsyncObserver<OnResponseEventArgs>
    {
        Task Handle(
            IReadOnlyRequest request,
            IReadOnlyResponse response,
            CancellationToken token = default);
    }

}
