using Charon.Dns.Net;
using Charon.Dns.Settings;
using Charon.Dns.SystemCommands;
using Charon.Dns.SystemCommands.Implementations;

namespace Charon.Dns
{
    public class ServiceInitializer(
        ICommandRunner commandRunner,
        ListeningSettings listeningSettings,
        DnsChainSettings chainSettings,
        RoutingSettings routingSettings)
    {
        public async Task Initialize()
        {
            foreach (var listeningSettingsItem in listeningSettings.Items)
            {
                var index = 0;
                if (!listeningSettingsItem.DebugOnly)
                {
                    await commandRunner.Execute(new AddInterfaceForDnsCommand
                    {
                        InterfaceIndex = index,
                    });
                    
                    await commandRunner.Execute(new SetIpForDnsInterfaceCommand
                    {
                        InterfaceIndex = index,
                        InterfaceAddress = listeningSettingsItem.Address,
                    });
                    
                    index++;
                }
            }
            
            foreach (var securedDnsServer in chainSettings.SecuredServers)
            {
                await commandRunner.Execute(new AddIpV4RouteCommand
                {
                    Ip = new IpV4Network(securedDnsServer.GetAddressBytes(), 32),
                    Interface = routingSettings.InterfaceToRouteThrough,
                });
            }
        }
    }
}
