using System.Threading;
using System.Threading.Tasks;
using Charon.Dns.Lib.Protocol;

namespace Charon.Dns.Lib.Client.RequestResolver
{
    public interface IRequestResolver
    {
        Task<IResponse> Resolve(IRequest request, CancellationToken cancellationToken = default(CancellationToken));
    }
}
