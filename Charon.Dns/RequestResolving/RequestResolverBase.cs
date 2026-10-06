using System.Diagnostics;
using System.Net;
using Charon.Dns.Lib.Client.RequestResolver;
using Charon.Dns.Lib.Protocol;
using Charon.Dns.Lib.Tracing;
using Charon.Dns.RequestResolving.ResolvingStrategies;
using Charon.Dns.Utils.Units;
using Serilog;

namespace Charon.Dns.RequestResolving;

public class RequestResolverBase : IRequestResolver
{
    private readonly IResolvingStrategy _resolvingStrategy;
    private readonly UdpRequestResolver[] _innerResolvers;

    public RequestResolverBase(
        IResolvingStrategy resolvingStrategy,
        IEnumerable<IPEndPoint> chainDnsServers,
        ByteUnit socketBufferSize,
        ILogger logger)
    {
        _resolvingStrategy = resolvingStrategy;
        _innerResolvers = chainDnsServers
            .Select(x => new UdpRequestResolver(x, socketBufferSize, logger))
            .ToArray();
    }

    public async Task<IResponse> Resolve(
        IRequest request, 
        RequestTrace trace, 
        CancellationToken cancellationToken = default)
    {
        var resolver = GetType().Name;
        trace.Logger.Debug("Resolving {@Request} by {Resolver}", request, resolver);
        
        var stopwatch = Stopwatch.StartNew();
        try
        {
            return await _resolvingStrategy.Resolve(
                _innerResolvers,
                request,
                trace,
                cancellationToken);
        }
        finally
        {
            trace.Logger.Debug(
                "{Source}: request handled by chain in {ElapsedMilliseconds} ms.", 
                GetType().Name, 
                stopwatch.ElapsedMilliseconds);
        }
    }
}
