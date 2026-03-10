namespace Charon.Dns.Routing;

public interface IRouteManager<T>
{
    Task AddRoute(T ip, string interfaceName);
    Task RemoveRoute(RouteToUntrack<T> routeToUntrack);
}