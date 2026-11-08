using Charon.Dns.Net;
using Microsoft.Extensions.DependencyInjection;

namespace Charon.Dns.Routing;

public static class RoutingDiExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddRouteManagement()
        {
            return services
                .AddSingleton<IRouteManager<IpV4Network>, IpRouteManager<IpV4Network>>()
                .AddSingleton<IRouteManager<IpV6Network>, IpRouteManager<IpV6Network>>()
                .AddSingleton<IRouteUsageTracker<IpV4Network>, RouteUsageTracker<IpV4Network>>()
                .AddSingleton<IRouteUsageTracker<IpV6Network>, RouteUsageTracker<IpV6Network>>();
        }
    }
}
