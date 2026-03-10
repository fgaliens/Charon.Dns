using System.Collections.Concurrent;
using Charon.Dns.Settings;
using Charon.Dns.Utils;
using Serilog;

namespace Charon.Dns.Routing;

public class RouteUsageTracker<T> : IRouteUsageTracker<T> where T : notnull
{
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly RoutingSettings _routingSettings;
    private readonly ILogger _logger;
    // TODO: ImmutableDictionary?
    private readonly ConcurrentDictionary<T, RouteItem> _ipNetworks = new();

    public RouteUsageTracker(
        IDateTimeProvider dateTimeProvider,
        RoutingSettings routingSettings,
        ILogger logger)
    {
        _dateTimeProvider = dateTimeProvider;
        _routingSettings = routingSettings;
        _logger = logger;
    }
    
    public async ValueTask<bool> TryTrackRoute(T ip)
    {
        if (_ipNetworks.TryGetValue(ip, out var item))
        {
            await item.EnterLock();
            using (item);
            
            var itemIsTracked = item.State == RouteState.Active;
            if (item.State == RouteState.Removing)
            {
                _logger.Warning("Invalid state of route while trying to add: {Ip} - {Item}", ip, item);
            }
            
            item.State = RouteState.Active;
            item.LastUsageTime = _dateTimeProvider.UtcNow;
            
            return !itemIsTracked;
        }

        return _ipNetworks.TryAdd(ip, new RouteItem
        {
            LastUsageTime = _dateTimeProvider.UtcNow,
            State = RouteState.Active,
        });
    }

    public async Task<RouteToUntrack<T>> FindNextRouteToUntrack()
    {
        var outdatedPeriod = _dateTimeProvider.UtcNow - _routingSettings.RoutingPeriod;
        foreach (var (ipNetwork, routeItem) in _ipNetworks)
        {
            if (outdatedPeriod > routeItem.LastUsageTime && routeItem.State == RouteState.Active)
            {
                await routeItem.EnterLock();
                if (outdatedPeriod > routeItem.LastUsageTime && routeItem.State == RouteState.Active)
                {
                    routeItem.State = RouteState.Removing;
                    return new RouteToUntrackInternal(routeItem)
                    {
                        Found = true,
                        Route = ipNetwork,
                    };
                }
            }
        }
        
        return new RouteToUntrackInternal(null)
        {
            Found = false,
            Route = default,
        };
    }
    
    public bool RemoveRouteFromTracking(RouteToUntrack<T> routeToUntrack)
    {
        if (!routeToUntrack.Found)
        {
            return false;
        }
        
        var ip = routeToUntrack.Route;
        if (_ipNetworks.TryGetValue(ip, out var item))
        {
            if (item.State == RouteState.Active)
            {
                _logger.Warning("Invalid state of route while trying to untrack: {Ip} - {Item}", ip, item);
                return false;
            }
            
            item.State = RouteState.Removed;
            return true;
        }
        
        _logger.Warning("Route has never been tracked before. Unable to untrack: {Ip}", ip);
        return false;
    }
    
    private class RouteItem : IDisposable
    {
        private readonly SemaphoreSlim _lock = new(1, 1);
         
        public DateTimeOffset LastUsageTime { get; set; }
        public RouteState State { get; set; }

        public async Task EnterLock()
        {
            await _lock.WaitAsync();
        }
        
        public void Dispose()
        {
            _lock.Release();
        }
    }

    private enum RouteState
    {
        Active,
        Removing,
        Removed,
    }
    
    private class RouteToUntrackInternal(RouteItem? routeItem) : RouteToUntrack<T>
    {
        public override void Dispose()
        {
            routeItem?.Dispose();
        }
    }
}
