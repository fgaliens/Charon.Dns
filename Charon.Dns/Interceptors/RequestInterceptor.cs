using System.Collections.Concurrent;
using Charon.Dns.Lib.Protocol;
using Charon.Dns.Net;
using Charon.Dns.RequestResolving;
using Charon.Dns.Routing;

namespace Charon.Dns.Interceptors
{
    public class RequestInterceptor(
        IHostNameAnalyzer hostNameAnalyzer,
        IRouteManager<IpV4Network> ipV4NetworkManager,
        IRouteManager<IpV6Network> ipV6NetworkManager) 
        : IRequestInterceptor
    {
        public async Task Handle(IRequest request, IResponse response, CancellationToken token = default)
        {
            var previousHostNameWasSecured = false;
            Domain? previousHostName = null;
            SecuredConnectionParams? connectionParams = null;
            
            foreach (var answer in response.AnswerRecords)
            {
                var shouldBeSecured = false;

                if (answer.Name.Equals(previousHostName))
                {
                    shouldBeSecured = previousHostNameWasSecured;
                }
                else
                {
                    shouldBeSecured = hostNameAnalyzer.ShouldBeSecured(answer.Name.ToString(), out connectionParams);
                    previousHostName = answer.Name;
                    previousHostNameWasSecured = shouldBeSecured;
                }
                
                if (shouldBeSecured)
                {
                    if (answer.Type is RecordType.A)
                    {
                        var ipV4Network = new IpV4Network(answer.Data, connectionParams!.IpV4RoutingSubnet);
                        await ipV4NetworkManager.AddRoute(ipV4Network, connectionParams.InterfaceName);
                    }
                    else if (answer.Type is RecordType.AAAA)
                    {
                        var ipV6Network = new IpV6Network(answer.Data, connectionParams!.IpV6RoutingSubnet);
                        await ipV6NetworkManager.AddRoute(ipV6Network, connectionParams.InterfaceName);
                    }
                }
            }
        }
    }
}
