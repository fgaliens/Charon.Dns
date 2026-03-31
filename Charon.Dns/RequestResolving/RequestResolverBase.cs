using System.Diagnostics;
using System.Net;
using Charon.Dns.Lib.Client.RequestResolver;
using Charon.Dns.Lib.Protocol;
using Charon.Dns.Lib.Tracing;
using Charon.Dns.RequestResolving.ResolvingStrategies;
using Charon.Dns.Utils.ByteUnits;

namespace Charon.Dns.RequestResolving;

public class RequestResolverBase : IRequestResolver
{
    private const int DefaultDnsPort = 53; 
        
    private readonly IResolvingStrategy _resolvingStrategy;
    private readonly UdpRequestResolver[] _innerResolvers;

    public RequestResolverBase(
        IResolvingStrategy resolvingStrategy,
        IEnumerable<IPAddress> chainDnsServers,
        ByteUnit socketBufferSize,
        int requestConcurrencyLimit)
    {
        _resolvingStrategy = resolvingStrategy;
        _innerResolvers = chainDnsServers
            .Select(x => new UdpRequestResolver(
                new IPEndPoint(x, DefaultDnsPort), 
                requestConcurrencyLimit,
                socketBufferSize))
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
