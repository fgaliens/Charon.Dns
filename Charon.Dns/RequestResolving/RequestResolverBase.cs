using System.Diagnostics;
using System.Net;
using Charon.Dns.Lib.Client.RequestResolver;
using Charon.Dns.Lib.Protocol;
using Charon.Dns.Lib.Tracing;
using Charon.Dns.Utils;

namespace Charon.Dns.RequestResolving;

public class RequestResolverBase : IRequestResolver
{
    private const int DefaultDnsPort = 53; 
        
    private readonly UdpRequestResolver[] _innerResolvers;
    private readonly RequestCounter _counter = new();
        
    public RequestResolverBase(IEnumerable<IPAddress> chainDnsServers, int requestConcurrencyLimit)
    {
        _innerResolvers = chainDnsServers
            .Select(x => new UdpRequestResolver(new IPEndPoint(x, DefaultDnsPort), requestConcurrencyLimit))
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
            // TODO: Remove
            var resolverIndex = _counter.Increment() % (ulong)_innerResolvers.Length;
            return await _innerResolvers[resolverIndex]
                .Resolve(request, trace, cancellationToken);
            
            //TODO: uncomment
            // var responseTasks = _innerResolvers.Select(x => 
            //     x.Resolve(request, trace, cancellationToken));
            // var response = await Task.WhenAny(responseTasks);
            // return await response;
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
