using System.Diagnostics;
using System.Net;
using Charon.Dns.Lib.Client.RequestResolver;
using Charon.Dns.Lib.Protocol;
using Charon.Dns.Settings;
using Serilog;

namespace Charon.Dns.RequestResolving
{
    public class DefaultRequestResolver : IDefaultRequestResolver
    {
        private readonly ILogger _logger;
        private readonly UdpRequestResolver[] _innerResolvers;
        private const int DefaultDnsPort = 53; 
        
        public DefaultRequestResolver(
            DnsChainSettings dnsChainSettings,
            ILogger logger)
        {
            _logger = logger;
            _innerResolvers = dnsChainSettings
                .DefaultServers
                .Select(x => new UdpRequestResolver(new IPEndPoint(x, DefaultDnsPort)))
                .ToArray();
        }

        public async Task<IResponse> Resolve(IRequest request, IPEndPoint remoteEndPoint, CancellationToken cancellationToken = default)
        {
            _logger.Debug("Resolving {@Request} by default", request);

            var stopwatch = Stopwatch.StartNew();
            
            try
            {
                var responseTasks = _innerResolvers.Select(x => x.Resolve(request, remoteEndPoint, cancellationToken));
                var response = await Task.WhenAny(responseTasks);
                return await response;
            }
            finally
            {
                _logger.Debug(
                    "{Source}: request resolved by chain in {ElapsedMilliseconds}", 
                    nameof(DefaultRequestResolver), 
                    stopwatch.ElapsedMilliseconds);
            }
        }
    }
}
