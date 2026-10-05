using Charon.Dns.AccessControl;
using Charon.Dns.Jobs;
using Charon.Dns.Lib.Tracing;
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
                if (!listeningSettingsItem.DebugOnly)
                {
                    await commandRunner.Execute(new AddInterfaceForDnsCommand
                    {
                        InterfaceIndex = index,
                    }, RequestTrace.Empty);
                    
                    await commandRunner.Execute(new SetIpForDnsInterfaceCommand
                    {
                        InterfaceIndex = index,
                        InterfaceAddress = listeningSettingsItem.Address,
                    }, RequestTrace.Empty);
                    
                    index++;
                }
            }
            
            foreach (var securedDnsServer in chainSettings.SecuredServers)
            {
                await commandRunner.Execute(new AddIpRouteCommand<IpV4Network>
                {
                    Ip = new IpV4Network(securedDnsServer.Ip.GetAddressBytes(), 32),
                    Interface = securedDnsServer.InterfaceToRouteThrough,
                }, RequestTrace.Empty);
            }
            
            await accessControlManager.RevertAllTaggedRules();

            jobRunner.Start();
        }
    }
}
