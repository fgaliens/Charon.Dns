using System.Threading;
using System.Threading.Tasks;
using Charon.Dns.Lib.Protocol;
using Charon.Dns.Lib.Tracing;

namespace Charon.Dns.Lib.Client.RequestResolver;

public class NullRequestResolver : IRequestResolver
{
    public Task<IResponse> Resolve(IRequest request, RequestTrace trace, CancellationToken cancellationToken = default)
    {
        throw new ResponseException("Request failed");
    }
}
