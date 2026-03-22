using Charon.Dns.Net;
using Charon.Dns.SystemCommands;
using Charon.Dns.SystemCommands.Implementations;
using Charon.Dns.Utils;
using Serilog;

namespace Charon.Dns.Routing;

public class IpRouteManager<T>(
    ICommandRunner commandRunner,
    IRouteUsageTracker<T> routeUsageTracker,
    ILogger logger) 
    : IRouteManager<T> 
    where T : IIpNetwork<T>
{
    public async Task AddRoutesBatch(IReadOnlyCollection<RouteBatchItem<T>> routeBatchItems)
    {
        if (routeBatchItems is { Count: 0 })
        {
            return;
        }
        
        if (routeBatchItems is { Count: 1 })
        {
            // Optimization for adding only one item
            var singleRouteItem = routeBatchItems.First();
            await AddSingleRoute(singleRouteItem.Ip.MinAddress, singleRouteItem.Interface);
            return;
        }
        
        var itemsGroup = routeBatchItems.GroupBy(x => x.Interface);

        foreach (var groupItem in itemsGroup)
        {
            var interfaceName = groupItem.Key;
            var ips = groupItem
                .Select(x => x.Ip.MinAddress)
                .ToHashSet();
            
            using var routes = new DisposableObjectsCollection<RouteToTrack<T>>(ips.Count);
            var untrackedRoutes = new List<T>(ips.Count);
            foreach (var ip in ips)
            {
                var routeToTrack = await routeUsageTracker.TryTrackRouteWithLock(ip);
                routes.Collection.Add(routeToTrack);

                if (!routeToTrack.TrackedAlready)
                {
                    untrackedRoutes.Add(ip);
                }
            }

            if (untrackedRoutes.Count == 0)
            {
                return;
            }

            await commandRunner.ExecuteBatch(untrackedRoutes.Select(x => new AddIpRouteCommand<T>
            {
                Ip = x,
                Interface = interfaceName,
            }));
        }
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
    
    private async Task AddSingleRoute(T ipNetwork, string interfaceName)
    {
        logger.Debug("Trying to add single route to {Ip} through {Interface}", ipNetwork, interfaceName);
        
        if (!await routeUsageTracker.TryTrackRoute(ipNetwork))
        {
            logger.Debug("Route to {Ip} through {Interface} has been added already", ipNetwork, interfaceName);
            return;
        }
        
        logger.Information("Adding single route to {Ip} through {Interface}", ipNetwork, interfaceName);
        
        await commandRunner.Execute(new AddIpRouteCommand<T>
        {
            Ip = ipNetwork,
            Interface = interfaceName,
        });
    }
}
