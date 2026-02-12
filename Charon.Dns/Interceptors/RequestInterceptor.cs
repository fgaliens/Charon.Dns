using System.Collections.Concurrent;
using Charon.Dns.Lib.Protocol;
using Charon.Dns.Net;
using Charon.Dns.RequestResolving;
using Charon.Dns.SystemCommands;
using Charon.Dns.SystemCommands.Implementations;

namespace Charon.Dns.Interceptors
{
    public class RequestInterceptor(
        IHostNameAnalyzer hostNameAnalyzer,
        ICommandRunner commandRunner) 
        : IRequestInterceptor
    {
        private readonly ConcurrentDictionary<IpV4Network, bool> _addedIpV4Networks = new();
        private readonly ConcurrentDictionary<IpV6Network, bool> _addedIpV6Networks = new();
        
        public Task Handle(IRequest request, IResponse response, CancellationToken token = default)
        {
            foreach (var answer in response.AnswerRecords)
            {
                if (hostNameAnalyzer.ShouldBeSecured(answer.Name.ToString(), out var connectionParams))
                {
                    if (answer.Type is RecordType.A)
                    {
                        var ipV4Network = new IpV4Network(answer.Data, connectionParams.IpV4RoutingSubnet)
                            .MinAddress;
                        
                        if (_addedIpV4Networks.TryAdd(ipV4Network, true))
                        {
                            _ = commandRunner.Execute(new AddIpV4RouteCommand
                            {
                                Ip = ipV4Network,
                                Interface = connectionParams.InterfaceName,
                            }, token);
                        }
                    }
                    else if (answer.Type is RecordType.AAAA)
                    {
                        var ipV6Network = new IpV6Network(answer.Data, connectionParams.IpV6RoutingSubnet)
                            .MinAddress;

                        if (_addedIpV6Networks.TryAdd(ipV6Network, true))
                        {
                            _ = commandRunner.Execute(new AddIpV6RouteCommand
                            {
                                Ip = ipV6Network,
                                Interface = connectionParams.InterfaceName,
                            }, token);
                        }
                    }
                }
            }
            
            return Task.CompletedTask;
        }
    }
}
