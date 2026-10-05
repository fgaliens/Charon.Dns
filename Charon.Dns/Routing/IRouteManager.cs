using Charon.Dns.Net;

namespace Charon.Dns.Routing;

public interface IRouteManager<T> where T : IIpNetwork<T>
{
    Task AddRoute(T ip, string interfaceName);
    Task RemoveRoute(RouteToUntrack<T> routeToUntrack);
}