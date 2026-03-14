using Charon.Dns.Net;
using Charon.Dns.Routing;
using Charon.Dns.Settings;

namespace Charon.Dns.Jobs.Implementations;

public class RemoveOutdatedRoutesJob(
    IRouteManager<IpV4Network> ipV4NetworkManager,
    IRouteManager<IpV6Network> ipV6NetworkManager,
    IRouteUsageTracker<IpV4Network> ipV4NetworkUsageTracker,
    IRouteUsageTracker<IpV6Network> ipV6NetworkUsageTracker,
    RoutingSettings routingSettings) 
    : IJob
{

    public TimeSpan Period { get; } = routingSettings.RoutingPeriod / 2;
    
    public async Task Execute()
    {
        var ipV4RouteFound = true;
        while (ipV4RouteFound)
        {
            using var routeToUntrack = await ipV4NetworkUsageTracker.FindNextRouteToUntrack();
            if (routeToUntrack.Found)
            {
                await ipV4NetworkManager.RemoveRoute(routeToUntrack);
            }
            ipV4RouteFound = routeToUntrack.Found;
        }
        
        var ipV6RouteFound = true;
        while (ipV6RouteFound)
        {
            using var routeToUntrack = await ipV6NetworkUsageTracker.FindNextRouteToUntrack();
            if (routeToUntrack.Found)
            {
                await ipV6NetworkManager.RemoveRoute(routeToUntrack);
            }
            ipV6RouteFound = routeToUntrack.Found;
        }
    }
}
