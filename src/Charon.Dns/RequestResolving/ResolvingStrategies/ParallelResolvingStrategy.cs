using Charon.Dns.Lib.Client.RequestResolver;
using Charon.Dns.Lib.Protocol;
using Charon.Dns.Lib.Tracing;

namespace Charon.Dns.RequestResolving.ResolvingStrategies;

public class ParallelResolvingStrategy : IResolvingStrategy
{
    public ResolvingStrategy Strategy { get; } = ResolvingStrategy.Parallel;
    
    public async Task<IResponse> Resolve(
        IReadOnlyList<IRequestResolver> resolvers,
        IRequest request, 
        RequestTrace trace,
        CancellationToken cancellationToken = default)
    {
        var responseTasks = resolvers.Select(x => 
            x.Resolve(request, trace, cancellationToken));
        var response = await Task.WhenAny(responseTasks);
        return await response;
    }
}
