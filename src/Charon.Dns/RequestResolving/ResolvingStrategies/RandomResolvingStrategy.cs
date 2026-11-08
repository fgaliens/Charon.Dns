using Charon.Dns.Lib.Client.RequestResolver;
using Charon.Dns.Lib.Protocol;
using Charon.Dns.Lib.Tracing;

namespace Charon.Dns.RequestResolving.ResolvingStrategies;

public class RandomResolvingStrategy : IResolvingStrategy
{
    public ResolvingStrategy Strategy { get; } = ResolvingStrategy.Random;
    
    public async Task<IResponse> Resolve(
        IReadOnlyList<IRequestResolver> resolvers,
        IRequest request, 
        RequestTrace trace,
        CancellationToken cancellationToken = default)
    {
        var resolverIndex = Random.Shared.Next(resolvers.Count);
        var resolver = resolvers[resolverIndex];
        return await resolver.Resolve(request, trace, cancellationToken);
    }
}
