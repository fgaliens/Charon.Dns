using Charon.Dns.Lib.Protocol;
using Charon.Dns.RequestResolving;
using Charon.Dns.Settings;
using Charon.Dns.SystemCommands;
using Charon.Dns.SystemCommands.Implementations;

namespace Charon.Dns.Interceptors
{
    public class RequestInterceptor(
        IHostNameAnalyzer hostNameAnalyzer,
        ICommandRunner commandRunner,
        RoutingSettings routingSettings) : IRequestInterceptor
    {
        public Task Handle(IRequest request, IResponse response, CancellationToken token = default)
        {
            foreach (var answer in response.AnswerRecords)
            {
                if (hostNameAnalyzer.ShouldBeSecured(answer.Name))
                {
                    if (answer.Type is RecordType.A)
                    {
                        _ = commandRunner.Execute(new AddIpV4RouteCommand
                        {
                            Ip = new(answer.Data, routingSettings.IpV4RoutingSubnet),
                            Interface = routingSettings.InterfaceToRouteThrough,
                        }, token);
                    }
                    else if (answer.Type is RecordType.AAAA)
                    {
                        _ = commandRunner.Execute(new AddIpV6RouteCommand
                        {
                            Ip = new(answer.Data, routingSettings.IpV6RoutingSubnet),
                            Interface = routingSettings.InterfaceToRouteThrough,
                        }, token);
                    }
                }
            }
            
            return Task.CompletedTask;
        }
    }
}
