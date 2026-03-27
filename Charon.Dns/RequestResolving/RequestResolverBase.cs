using System.Diagnostics;
using System.Net;
using Charon.Dns.Lib.Client.RequestResolver;
using Charon.Dns.Lib.Protocol;
using Charon.Dns.Lib.Tracing;
using Charon.Dns.Utils;
using Serilog;

namespace Charon.Dns.RequestResolving;

public class RequestResolverBase : IRequestResolver
{
    private const int DefaultDnsPort = 53; 
        
    private readonly UdpRequestResolver[] _innerResolvers;
    private readonly RequestCounter _counter;

    public RequestResolverBase(IEnumerable<IPAddress> chainDnsServers, ILogger globalLogger)
    {
        _innerResolvers = chainDnsServers
            .Select(x => new UdpRequestResolver(new IPEndPoint(x, DefaultDnsPort), globalLogger))
            .ToArray();
        _counter = new RequestCounter();
    }

    public async Task<IResponse> Resolve(
        IRequest request, 
        RequestTrace trace, 
        CancellationToken cancellationToken = default)
    {
        trace.Logger.Debug("Resolving {@Request} safely", request);
            
        var stopwatch = Stopwatch.StartNew();
        try
        {
            return await  _innerResolvers[Random.Shared.Next(_innerResolvers.Length)]
                .Resolve(request, trace, cancellationToken);
            
            //TODO: uncomment
            // var responseTasks = _innerResolvers.Select(x => 
            //     x.Resolve(request, trace, cancellationToken));
            // var response = await Task.WhenAny(responseTasks);
            // return await response;
        }
        finally
        {
            trace?.Logger.Debug(
                "{Source}: request resolved by chain in {ElapsedMilliseconds} ms.", 
                GetType().Name, 
                stopwatch.ElapsedMilliseconds);
        }
    }
}
