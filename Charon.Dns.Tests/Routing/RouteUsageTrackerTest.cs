using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoFixture;
using Charon.Dns.Net;
using Charon.Dns.Routing;
using Charon.Dns.Settings;
using Charon.Dns.Tests.Utils.Mock;
using Charon.Dns.Utils;
using FluentAssertions;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Xunit;

namespace Charon.Dns.Tests.Routing;

[TestSubject(typeof(RouteUsageTracker<>))]
public class RouteUsageTrackerTest
{
    private static readonly TimeSpan DefaultRoutingPeriod = TimeSpan.FromHours(1);
    private readonly Fixture _fixture = new();

    public RouteUsageTrackerTest()
    {
        byte ipV4Counter = 0;
        _fixture.Register(() =>
        {
            ipV4Counter++;
            var ipBytes = Enumerable.Repeat(ipV4Counter, 4).ToArray();
            var randomSubnet = (byte)Random.Shared.Next(16, 33);
            
            return new IpV4Network(ipBytes, randomSubnet);
        });
        
        byte ipV6Counter = 0;
        _fixture.Register(() =>
        {
            ipV6Counter++;
            var ipBytes = Enumerable.Repeat(ipV6Counter, 16).ToArray();
            var randomSubnet = (byte)Random.Shared.Next(64, 129);
            
            return new IpV6Network(ipBytes, randomSubnet);
        });
    }
    
    [Fact]
    public async Task TryTrackRoute_UnableToAddRouteTwice()
    {
        await TryTrackRoute_UnableToAddRouteTwice_Generic<IpV4Network>();
        await TryTrackRoute_UnableToAddRouteTwice_Generic<IpV6Network>();
    }

    private async Task TryTrackRoute_UnableToAddRouteTwice_Generic<T>() where T : IIpNetwork<T>
    {
        // Arrange
        var services = GetServiceProvider<T>();
        var routeUsageTracker = services.GetRequiredService<RouteUsageTracker<T>>();
        
        var ip = _fixture.Create<T>();
        
        // Act
        var result1 = await routeUsageTracker.TryTrackRoute(ip);
        var result2 = await routeUsageTracker.TryTrackRoute(ip);
        
        // Assert
        result1.Should().BeTrue();
        result2.Should().BeFalse();
    }
    
    [Fact]
    public async Task TryTrackRoute_UnableToAddRouteTwiceSimultaneously()
    {
        await TryTrackRoute_UnableToAddRouteTwiceSimultaneously_Generic<IpV4Network>();
        await TryTrackRoute_UnableToAddRouteTwiceSimultaneously_Generic<IpV6Network>();
    }
    
    [SuppressMessage("ReSharper", "AccessToDisposedClosure")]
    private async Task TryTrackRoute_UnableToAddRouteTwiceSimultaneously_Generic<T>() where T : IIpNetwork<T>
    {
        // Arrange
        var services = GetServiceProvider<T>();
        var routeUsageTracker = services.GetRequiredService<RouteUsageTracker<T>>();
        
        var ip = _fixture.Create<T>();

        using var sync = new Barrier(2);

        bool? result1 = null;
        bool? result2 = null;
        
        // Act
        var task1 = Task.Run(async () =>
        {
            sync.SignalAndWait();
            result1 = await routeUsageTracker.TryTrackRoute(ip);
        });
        
        var task2 = Task.Run(async () =>
        {
            sync.SignalAndWait();
            result2 = await routeUsageTracker.TryTrackRoute(ip);
        });
        
        await Task.WhenAll(task1, task2);
        
        // Assert
        result1.Should().NotBeNull();
        result2.Should().NotBeNull();
        (result1!.Value ^ result2!.Value).Should().BeTrue();
    }
    
    [Fact]
    public async Task FindNextRouteToUntrack_WhenThereIsNoOutdatedRoutes()
    {
        await FindNextRouteToUntrack_WhenThereIsNoOutdatedRoutes_Generic<IpV4Network>();
        await FindNextRouteToUntrack_WhenThereIsNoOutdatedRoutes_Generic<IpV6Network>();
    }
    
    private async Task FindNextRouteToUntrack_WhenThereIsNoOutdatedRoutes_Generic<T>() where T : IIpNetwork<T>
    {
        // Arrange
        var services = GetServiceProvider<T>();
        var routeUsageTracker = services.GetRequiredService<RouteUsageTracker<T>>();
        
        var ip1 = _fixture.Create<T>();
        var ip2 = _fixture.Create<T>();
        var ip3 = _fixture.Create<T>();

        var route1Tracked = await routeUsageTracker.TryTrackRoute(ip1);
        var route2Tracked = await routeUsageTracker.TryTrackRoute(ip2);
        var route3Tracked = await routeUsageTracker.TryTrackRoute(ip3);
        
        // Act
        var routeToUntrack = await routeUsageTracker.FindNextRouteToUntrack();
        
        // Assert
        route1Tracked.Should().BeTrue();
        route2Tracked.Should().BeTrue();
        route3Tracked.Should().BeTrue();
        routeToUntrack.Found.Should().BeFalse();
    }
    
    [Fact]
    public async Task FindNextRouteToUntrack_WhenThereIsOneOutdatedRoute()
    {
        await FindNextRouteToUntrack_WhenThereIsOneOutdatedRoute_Generic<IpV4Network>();
        await FindNextRouteToUntrack_WhenThereIsOneOutdatedRoute_Generic<IpV6Network>();
    }
    
    private async Task FindNextRouteToUntrack_WhenThereIsOneOutdatedRoute_Generic<T>() where T : IIpNetwork<T>
    {
        // Arrange
        var services = GetServiceProvider<T>();
        var routeUsageTracker = services.GetRequiredService<RouteUsageTracker<T>>();
        
        var ip1 = _fixture.Create<T>();
        var ip2 = _fixture.Create<T>();
        var ip3 = _fixture.Create<T>();
        
        var baseTime = DateTimeOffset.UtcNow;
        var outdated = baseTime - DefaultRoutingPeriod * 1.25;
        var valid = baseTime;

        services.SetupMockOf<IDateTimeProvider>(mock => mock
            .SetupSequence(x => x.UtcNow)
            .Returns(outdated)
            .Returns(valid)
            .Returns(valid)
            .Returns(valid)
            .Returns(valid)
            .Returns(valid));

        var route1Tracked = await routeUsageTracker.TryTrackRoute(ip1);
        var route2Tracked = await routeUsageTracker.TryTrackRoute(ip2);
        var route3Tracked = await routeUsageTracker.TryTrackRoute(ip3);
        
        // Act
        var routeToUntrack = await routeUsageTracker.FindNextRouteToUntrack();
        var nextRouteToUntrack = await routeUsageTracker.FindNextRouteToUntrack();
        
        // Assert
        route1Tracked.Should().BeTrue();
        route2Tracked.Should().BeTrue();
        route3Tracked.Should().BeTrue();
        
        routeToUntrack.Found.Should().BeTrue();
        routeToUntrack.Route.Should().Be(ip1);
        
        nextRouteToUntrack.Found.Should().BeFalse();
    }

    private IServiceProvider GetServiceProvider<T>() where T : IIpNetwork<T>
    {
        return new ServiceCollection()
            .AddSingleton<RouteUsageTracker<T>>()
            .AddMockOf<IDateTimeProvider>(mock => mock
                .SetupGet(x => x.UtcNow)
                .Returns(DateTime.UtcNow))
            .AddMockOf<ILogger>()
            .AddSettings(new RoutingSettings
            {
                RoutingPeriod = DefaultRoutingPeriod,
                Items = [],
                BlockedHostNames = [],
            })
            .BuildServiceProvider();
    }
}
