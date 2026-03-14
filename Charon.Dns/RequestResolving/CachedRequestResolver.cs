using Charon.Dns.Cache;
using Charon.Dns.Lib.Protocol;

namespace Charon.Dns.RequestResolving;

public class CachedRequestResolver(IDnsCache dnsCache) : ICachedRequestResolver
{
    public Task<IResponse> Resolve(IRequest request, CancellationToken cancellationToken = default(CancellationToken))
    {
        if (dnsCache.TryGetResponse(request, out var response))
        {
            return Task.FromResult(response);
        }
        
        return Task.FromResult<IResponse>(Response.FromRequest(request));
    }
}
