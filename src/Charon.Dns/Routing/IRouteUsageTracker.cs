namespace Charon.Dns.Routing;

public interface IRouteUsageTracker<T>
{
    ValueTask<bool> TryTrackRoute(T ip);
    Task<RouteToUntrack<T>> FindNextRouteToUntrack();
    bool RemoveRouteFromTracking(RouteToUntrack<T> routeToUntrack);
}
