using Charon.Dns.Lib.Client.RequestResolver;
using Charon.Dns.Lib.Protocol;
using Charon.Dns.Lib.Tracing;

namespace Charon.Dns.RequestResolving.ResolvingStrategies;

public interface IResolvingStrategy
{
    ResolvingStrategy Strategy { get; }

    Task<IResponse> Resolve(
        IReadOnlyList<IRequestResolver> resolvers,
        IRequest request,
        RequestTrace trace,
        CancellationToken cancellationToken = default);
}