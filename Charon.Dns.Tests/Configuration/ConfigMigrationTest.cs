using System;
using System.IO;
using System.Linq;
using System.Net;
using Charon.Dns.Configuration;
using Charon.Dns.Configuration.Migrations;
using Charon.Dns.Settings;
using FluentAssertions;
using JetBrains.Annotations;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Charon.Dns.Tests.Configuration;

/// <summary>
/// Checks that the service reads configuration files of older versions after migration.
/// </summary>
[TestSubject(typeof(ConfigMigrator))]
public class ConfigMigrationTest : IDisposable
{
    private static readonly ConfigFileNames FileNames = ConfigFileNames.Production;

    private readonly string _directory;

    public ConfigMigrationTest()
    {
        var name = Guid.NewGuid().ToString("N");
        _directory = Path.Combine(Path.GetTempPath(), "charon-dns-tests", name);
        Directory.CreateDirectory(_directory);
    }

    [Fact]
    public void MigratedV1_5_12Settings_AreReadByService()
    {
        var sourcePath = Path.Combine(AppContext.BaseDirectory, "Configuration", "TestData", "settings.v1.5.12.json");
        var settingsPath = Path.Combine(_directory, FileNames.Settings);
        File.Copy(sourcePath, settingsPath);

        ConfigMigrator.Default.MigrateFiles(_directory, FileNames);
        var config = BuildConfiguration();

        config["Logging:Console"].Should().Be("Information");
        config["Logging:File"].Should().Be("Debug");

        var listening = ListeningSettings.Initialize(config);
        listening.MaxParallelRequestCount.Should().Be(8);
        listening.Items.Should().ContainSingle()
            .Which.Address.Should().Be(IPAddress.Parse("10.0.0.53"));

        var dnsChain = DnsChainSettings.Initialize(config);
        dnsChain.DefaultServers.Should().HaveCount(2);
        dnsChain.RoutedServers.Should().HaveCount(2)
            .And.OnlyContain(x => x.InterfaceToRouteThrough == "wg0");

        var cache = CacheSettings.Initialize(config);
        cache.TimeToLive.Should().Be(TimeSpan.FromMinutes(5));

        var routing = RoutingSettings.Initialize(config);
        routing.RouteIdleTimeout.Should().Be(TimeSpan.FromHours(2));
        routing.BlockedHostNames.Should().Contain("max.ru");
        var routingItem = routing.Items.Should().ContainSingle().Subject;
        routingItem.InterfaceToRouteThrough.Should().Be("wg0");
        routingItem.IpV4RoutingSubnet.Should().Be(32);
        routingItem.IpV6RoutingSubnet.Should().Be(96);
        routingItem.MatchedByDomainHostNames.Should().Contain("t.me");
        routingItem.MatchedBySubstringHostNames.Should().Contain("youtube");

        var zones = ZonesSettings.Initialize(config);
        zones.Zones.Select(x => x.Origin).Should().Equal("my.home", "my.hm");
        zones.Zones.Last().Records.Should().HaveCount(2)
            .And.ContainSingle(x => x.ResolveOnlyIfRequestCameFrom == "127.0.0.1");

        // 1.5.12 had no access control yet
        var userAccessControl = UserAccessControlSettings.Initialize(config);
        userAccessControl.Enabled.Should().BeFalse();
    }

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
    }

    private IConfiguration BuildConfiguration()
    {
        return new ConfigurationBuilder()
            .SetBasePath(_directory)
            .AddJsonFile(FileNames.Settings)
            .AddJsonFile(FileNames.Zones)
            .AddJsonFile(FileNames.Routing)
            .Build();
    }
}
