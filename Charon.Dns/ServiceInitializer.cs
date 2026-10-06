using Charon.Dns.AccessControl;
using Charon.Dns.Jobs;
using Charon.Dns.Net;
using Charon.Dns.Settings;
using Charon.Dns.SystemCommands;
using Charon.Dns.SystemCommands.Implementations;

namespace Charon.Dns
{
    public class ServiceInitializer(
        ICommandRunner commandRunner,
        IJobRunner jobRunner,
        IUserAccessControlManager accessControlManager,
        ListeningSettings listeningSettings,
        DnsChainSettings chainSettings)
    {
        public async Task Initialize()
        {
            var index = 0;
            
            foreach (var listeningSettingsItem in listeningSettings.Items)
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
            
            foreach (var securedDnsServer in chainSettings.SecuredServers)
            {
                await commandRunner.Execute(new AddIpRouteCommand<IpV4Network>
                {
                    Ip = new IpV4Network(securedDnsServer.Address.EndPoint.Address.GetAddressBytes(), 32),
                    Interface = securedDnsServer.InterfaceToRouteThrough,
                });
            }
            
            await accessControlManager.RevertAllTaggedRules();

            jobRunner.Start();
        }
    }
}
