using System.Collections.Immutable;
using Charon.Dns.Net;
using Charon.Dns.Settings;
using Charon.Dns.Utils;
using Serilog;

namespace Charon.Dns.Routing;

public class RouteUsageTracker<T> : IRouteUsageTracker<T> where T : IIpNetwork<T>
{
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly RoutingSettings _routingSettings;
    private readonly ILogger _logger;
    private ImmutableDictionary<T, RouteItem> _ipNetworks = ImmutableDictionary.Create<T, RouteItem>();

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

        return ImmutableInterlocked.TryAdd(ref _ipNetworks, ip, new RouteItem
        {
            State = RouteState.Active,
            LastUsageTime = _dateTimeProvider.UtcNow,
        });
    }

    public async Task<RouteToUntrack<T>> FindNextRouteToUntrack()
    {
        var outdatedPeriod = _dateTimeProvider.UtcNow - _routingSettings.RoutingPeriod;
        var ipNetworks = _ipNetworks;
        foreach (var (ipNetwork, routeItem) in ipNetworks)
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
        private static TimeSpan LockTimeout { get; } = TimeSpan.FromSeconds(5);
        private readonly SemaphoreSlim _lock = new(1, 1);
         
        public DateTimeOffset LastUsageTime { get; set; }
        public RouteState State { get; set; }
        public bool Locked => _lock.CurrentCount == 0;

        public async Task EnterLock()
        {
            var success = await _lock.WaitAsync(LockTimeout);
            if (!success)
            {
                throw new InvalidOperationException($"Getting lock for {nameof(RouteItem)} {this} failed");
            }
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
