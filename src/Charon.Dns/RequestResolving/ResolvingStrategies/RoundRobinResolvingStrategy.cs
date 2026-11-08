using Charon.Dns.Lib.Client.RequestResolver;
using Charon.Dns.Lib.Protocol;
using Charon.Dns.Lib.Tracing;
using Charon.Dns.Utils;

namespace Charon.Dns.RequestResolving.ResolvingStrategies;

public class RoundRobinResolvingStrategy : IResolvingStrategy
{
    private RequestCounter _requestCounter = new();
    
    public ResolvingStrategy Strategy { get; } = ResolvingStrategy.RoundRobin;
    
    public async Task<IResponse> Resolve(
        IReadOnlyList<IRequestResolver> resolvers,
        IRequest request, 
        RequestTrace trace,
        CancellationToken cancellationToken = default)
    {
        var requestCount = (int)(_requestCounter.Increment() % int.MaxValue);
        var resolverIndex = requestCount % resolvers.Count;
        var resolver = resolvers[resolverIndex];
        return await resolver.Resolve(request, trace, cancellationToken);
    }
}
