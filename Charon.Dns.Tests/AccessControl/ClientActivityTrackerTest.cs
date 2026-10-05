using System;
using System.Net;
using Charon.Dns.AccessControl;
using Charon.Dns.Tests.Utils.Mock;
using Charon.Dns.Utils;
using FluentAssertions;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Charon.Dns.Tests.AccessControl;

[TestSubject(typeof(ClientActivityTracker))]
public class ClientActivityTrackerTest
{
    [Fact]
    public void GetLastSeen_ReturnsNull_ForUnknownIp()
    {
        var tracker = GetServiceProvider().GetRequiredService<IClientActivityTracker>();

        tracker.GetLastSeen(IPAddress.Parse("10.8.0.2")).Should().BeNull();
    }

    [Fact]
    public void RecordActivity_ThenGetLastSeen_ReturnsCurrentTime()
    {
        var now = DateTimeOffset.UtcNow;
        var services = GetServiceProvider(mock => mock.SetupGet(x => x.UtcNow).Returns(now));
        var tracker = services.GetRequiredService<IClientActivityTracker>();
        var ip = IPAddress.Parse("10.8.0.2");

        tracker.RecordActivity(ip);

        tracker.GetLastSeen(ip).Should().Be(now);
    }

    [Fact]
    public void GetTrackedIps_ReturnsOnlyRecordedIps()
    {
        var tracker = GetServiceProvider().GetRequiredService<IClientActivityTracker>();
        var ip1 = IPAddress.Parse("10.8.0.2");
        var ip2 = IPAddress.Parse("10.8.0.3");

        tracker.RecordActivity(ip1);
        tracker.RecordActivity(ip2);

        tracker.GetTrackedIps().Should().BeEquivalentTo([ip1, ip2]);
    }

    private static IServiceProvider GetServiceProvider(Action<Moq.Mock<IDateTimeProvider>> configureClock = null)
    {
        return new ServiceCollection()
            .AddSingleton<IClientActivityTracker, ClientActivityTracker>()
            .AddMockOf(configureClock ?? (mock => mock.SetupGet(x => x.UtcNow).Returns(DateTimeOffset.UtcNow)))
            .BuildServiceProvider();
    }
}
