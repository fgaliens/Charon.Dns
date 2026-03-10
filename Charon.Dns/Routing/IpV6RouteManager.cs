using Charon.Dns.Net;
using Charon.Dns.SystemCommands;
using Charon.Dns.SystemCommands.Implementations;
using Serilog;

namespace Charon.Dns.Routing;

public class IpV6RouteManager(
    ICommandRunner commandRunner,
    IRouteUsageTracker<IpV6Network> routeUsageTracker,
    ILogger logger) 
    : IRouteManager<IpV6Network>
{
    public async Task AddRoute(IpV6Network ip, string interfaceName)
    {
        logger.Debug("Trying to add route to {Ip} through {Interface}", ip, interfaceName);
        
        var ipNetwork = ip.MinAddress;
        
        if (!await routeUsageTracker.TryTrackRoute(ipNetwork))
        {
            logger.Debug("Route to {Ip} through {Interface} has been added already", ip, interfaceName);
            return;
        }
        
        logger.Information("Adding route to {Ip} through {Interface}", ip, interfaceName);
        
        await commandRunner.Execute(new AddIpV6RouteCommand
        {
            Ip = ipNetwork,
            Interface = interfaceName,
        });
    }

    public async Task RemoveRoute(RouteToUntrack<IpV6Network> routeToUntrack)
    {
        logger.Information("Removing route to {Ip}", routeToUntrack.Route);
        
        if (routeUsageTracker.RemoveRouteFromTracking(routeToUntrack))
        {
            await commandRunner.Execute(new RemoveIpV6RouteCommand
            {
                Ip = routeToUntrack.Route,
            });
        }
    }
}
