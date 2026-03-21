using Charon.Dns.Net;
using Charon.Dns.SystemCommands;
using Charon.Dns.SystemCommands.Implementations;
using Serilog;

namespace Charon.Dns.Routing;

public class IpRouteManager<T>(
    ICommandRunner commandRunner,
    IRouteUsageTracker<T> routeUsageTracker,
    ILogger logger) 
    : IRouteManager<T> 
    where T : IIpNetwork<T>
{
    public async Task AddRoute(T ip, string interfaceName)
    {
        logger.Debug("Trying to add route to {Ip} through {Interface}", ip, interfaceName);
        
        var ipNetwork = ip.MinAddress;
        
        if (!await routeUsageTracker.TryTrackRoute(ipNetwork))
        {
            logger.Debug("Route to {Ip} through {Interface} has been added already", ip, interfaceName);
            return;
        }
        
        logger.Information("Adding route to {Ip} through {Interface}", ip, interfaceName);
        
        await commandRunner.Execute(new AddIpRouteCommand<T>
        {
            Ip = ipNetwork,
            Interface = interfaceName,
        });
    }

    public async Task RemoveRoute(RouteToUntrack<T> routeToUntrack)
    {
        logger.Information("Removing route to {Ip}", routeToUntrack.Route);
        
        if (routeUsageTracker.RemoveRouteFromTracking(routeToUntrack))
        {
            await commandRunner.Execute(new RemoveIpRouteCommand<T>
            {
                Ip = routeToUntrack.Route!,
            });
        }
    }
}
