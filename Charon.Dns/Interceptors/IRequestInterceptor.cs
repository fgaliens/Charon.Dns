using Charon.Dns.Lib.Protocol;
using Charon.Dns.Lib.Protocol.ResourceRecords;

namespace Charon.Dns.Interceptors
{
    public interface IRequestInterceptor
    {
        Task Handle(
            IRequest request,
            IResponse response,
            CancellationToken token = default);
    }

}
