using Charon.Dns.Net;

namespace Charon.Dns.Routing;

public interface IRouteManager<T> where T : IIpNetwork<T>
{
    Task AddRoutesBatch(IReadOnlyCollection<RouteBatchItem<T>> routeBatchItems);
    Task RemoveRoute(RouteToUntrack<T> routeToUntrack);
}