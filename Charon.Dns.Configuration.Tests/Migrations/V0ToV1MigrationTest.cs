using System.Text.Json.Nodes;
using Charon.Dns.Configuration.Migrations.V0ToV1;
using FluentAssertions;
using JetBrains.Annotations;
using Xunit;

namespace Charon.Dns.Configuration.Tests.Migrations;

[TestSubject(typeof(V0ToV1Migration))]
public class V0ToV1MigrationTest
{
    // Shortened settings.json of 1.5.x
    private const string V0Settings = """
        {
          // Comments are allowed
          "LogLevel": "Information",
          "FileLogLevel": "Debug",
          "Server": {
            "MaxParallelRequestCount": 8,
            "SocketBufferSize": "2Mb",
            "ListenOn": [
              { "Address": "0.0.0.0", "Port": 53, "DebugOnly": true, "EnableIpV6": false },
              { "Address": "10.0.0.53", "Port": 53, "DebugOnly": false, "EnableIpV6": true },
            ],
            "DnsChain": {
              "ResolvingStrategy": "RoundRobin",
              "ResolvingConcurrencyLimit": 2,
              "SocketBufferSize": "512Kb",
              "DefaultServers": [ "77.88.8.8" ],
              "SecuredServers": [ { "Ip": "1.1.1.1", "RouteThroughInterface": "wg0" } ]
            },
            "DnsRecords": {
              "A": [
                { "Name": "my.home", "Address": "10.11.1.2" },
                { "Name": "my.hm", "Address": "10.11.1.2" },
                { "Name": "my.hm", "Address": "192.168.1.1", "ResolveOnlyIfRequestCameFrom": "127.0.0.1" }
              ]
            }
          },
          "Cache": { "Enabled": true, "TimeToLive": "00:05:00" },
          "Routing": {
            "Period": "02:00:00",
            "BlockedHostNames": [ "max.ru", "file:hosts_blacklist.txt" ],
            "Items": [
              {
                "InterfaceToRouteThrough": "wg0",
                "IpV4RoutingSubnet": 32,
                "IpV6RoutingSubnet": 96,
                "HostNameMatches": {
                  "BySubstring": [ "youtube" ],
                  "ByDomainName": [ "t.me", "file:other_hosts.txt" ]
                }
              }
            ]
          },
          "UserAccessControl": {
            "Enabled": false,
            "InactivityThreshold": "00:30:00",
            "ControlledIps": [ "10.8.0.0/24" ]
          }
        }
        """;

    private const string ExpectedSettings = """
        {
          "Logging": { "Console": "Information", "File": "Debug" },
          "Server": {
            "SocketsPerEndpoint": 8,
            "SocketBufferSize": "2Mb",
            "ListenOn": [
              { "Address": "10.0.0.53", "Port": 53, "EnableIPv6": true }
            ]
          },
          "Upstream": {
            "ResolvingStrategy": "RoundRobin",
            "SocketBufferSize": "512Kb",
            "DefaultServers": [ "77.88.8.8" ],
            "RoutedServers": [ { "Address": "1.1.1.1", "Interface": "wg0" } ]
          },
          "Cache": { "Enabled": true, "Ttl": "00:05:00" },
          "UserAccessControl": {
            "Enabled": false,
            "InactivityThreshold": "00:30:00",
            "ControlledSubnets": [ "10.8.0.0/24" ]
          }
        }
        """;

    private const string ExpectedRouting = """
        {
          "Routing": {
            "RouteIdleTimeout": "02:00:00",
            "BlockedHosts": [ "max.ru", "file:hosts_blacklist.txt" ],
            "Rules": [
              {
                "Interface": "wg0",
                "IPv4PrefixLength": 32,
                "IPv6PrefixLength": 96,
                "Match": {
                  "Substrings": [ "youtube" ],
                  "Domains": [ "t.me", "file:other_hosts.txt" ]
                }
              }
            ]
          }
        }
        """;

    private const string ExpectedZones = """
        {
          "DefaultTtl": "00:05:00",
          "Zones": [
            {
              "Origin": "my.home",
              "Ttl": "00:05:00",
              "Records": [ { "Name": "@", "Type": "A", "Value": "10.11.1.2" } ]
            },
            {
              "Origin": "my.hm",
              "Ttl": "00:05:00",
              "Records": [
                { "Name": "@", "Type": "A", "Value": "10.11.1.2" },
                { "Name": "@", "Type": "A", "Value": "192.168.1.1", "ResolveOnlyIfRequestCameFrom": "127.0.0.1" }
              ]
            }
          ]
        }
        """;

    private const string ZonesTemplate = """
        {
          // Local DNS zones
          "DefaultTtl": "00:05:00",
          "Zones": []
        }
        """;

    [Fact]
    public void Migrate_ConvertsV0Files()
    {
        using var directory = new TemporaryConfigDirectory();
        directory.Write("settings.json", V0Settings);
        directory.Write("zones.json", ZonesTemplate);

        var result = directory.Migrate();

        result.FromVersion.Should().Be(0);
        result.ToVersion.Should().Be(1);
        ShouldBeJson(directory, "settings.json", ExpectedSettings);
        ShouldBeJson(directory, "routing.json", ExpectedRouting);
        ShouldBeJson(directory, "zones.json", ExpectedZones);
        ShouldBeJson(directory, "config.version.json", """{ "Version": 1 }""");
        directory.Read("settings.json.v0.bak").Should().Be(V0Settings);
        directory.Read("zones.json.v0.bak").Should().Be(ZonesTemplate);
        result.BackupFiles.Should().HaveCount(2);
        result.Notes.Should().Contain(x => x.Contains("0.0.0.0:53"));
    }

    [Fact]
    public void Migrate_KeepsOrderOfSettings()
    {
        using var directory = new TemporaryConfigDirectory();
        directory.Write("settings.json", V0Settings);
        directory.Write("zones.json", ZonesTemplate);

        directory.Migrate();

        var settings = directory.ReadJson("settings.json")!.AsObject();
        settings.Select(x => x.Key).Should().Equal("Logging", "Server", "Upstream", "Cache", "UserAccessControl");
    }

    [Fact]
    public void Migrate_DoesNotChangeFilesInCurrentFormat()
    {
        using var directory = new TemporaryConfigDirectory();
        directory.Write("settings.json", ExpectedSettings);
        directory.Write("routing.json", ExpectedRouting);
        directory.Write("zones.json", ExpectedZones);

        // No version file, as on a fresh installation without it
        var result = directory.Migrate();

        result.IsMigrated.Should().BeTrue();
        result.Notes.Should().BeEmpty();
        result.BackupFiles.Should().BeEmpty();
        directory.Read("settings.json").Should().Be(ExpectedSettings);
        directory.Read("routing.json").Should().Be(ExpectedRouting);
        directory.Read("zones.json").Should().Be(ExpectedZones);
        ShouldBeJson(directory, "config.version.json", """{ "Version": 1 }""");
    }

    [Fact]
    public void Migrate_IsRepeatable()
    {
        using var directory = new TemporaryConfigDirectory();
        directory.Write("settings.json", V0Settings);
        directory.Write("zones.json", ZonesTemplate);
        directory.Migrate();
        var migratedSettings = directory.Read("settings.json");
        var migratedZones = directory.Read("zones.json");

        // As if the version file was lost
        File.Delete(Path.Combine(directory.Path, "config.version.json"));
        directory.Migrate();

        directory.Read("settings.json").Should().Be(migratedSettings);
        directory.Read("zones.json").Should().Be(migratedZones);
    }

    [Fact]
    public void Migrate_AddsLegacyRecordsToExistingAndParentZones()
    {
        using var directory = new TemporaryConfigDirectory();
        directory.Write("settings.json", """
            {
              "Server": {
                "DnsRecords": {
                  "A": [
                    { "Name": "nas.Lan.", "Address": "10.0.0.3" },
                    { "Name": "nas.my.home", "Address": "10.0.0.2" },
                    { "Name": "my.home", "Address": "10.0.0.1" },
                    { "Name": "lan", "Address": "10.0.0.4" }
                  ]
                }
              },
              "Cache": { "TimeToLive": "00:01:00" }
            }
            """);
        directory.Write("zones.json", """
            {
              "Zones": [
                {
                  "Origin": "my.home",
                  "Records": [ { "Name": "@", "Type": "A", "Value": "10.0.0.1" } ]
                }
              ]
            }
            """);

        directory.Migrate();

        ShouldBeJson(directory, "zones.json", """
            {
              "Zones": [
                {
                  "Origin": "my.home",
                  "Records": [
                    { "Name": "@", "Type": "A", "Value": "10.0.0.1" },
                    { "Name": "nas.my.home.", "Type": "A", "Value": "10.0.0.2", "Ttl": "00:01:00" }
                  ]
                },
                {
                  "Origin": "lan",
                  "Ttl": "00:01:00",
                  "Records": [
                    { "Name": "@", "Type": "A", "Value": "10.0.0.4" },
                    { "Name": "nas.lan.", "Type": "A", "Value": "10.0.0.3" }
                  ]
                }
              ]
            }
            """);
    }

    [Fact]
    public void Migrate_CreatesZonesFileForLegacyRecords()
    {
        using var directory = new TemporaryConfigDirectory();
        directory.Write("settings.json", """
            { "Server": { "DnsRecords": { "A": [ { "Name": "my.home", "Address": "10.0.0.1" } ] } } }
            """);

        directory.Migrate();

        ShouldBeJson(directory, "zones.json", """
            { "Zones": [ { "Origin": "my.home", "Records": [ { "Name": "@", "Type": "A", "Value": "10.0.0.1" } ] } ] }
            """);
    }

    [Fact]
    public void Migrate_FailsIfRoutingIsSetInBothFiles()
    {
        using var directory = new TemporaryConfigDirectory();
        directory.Write("settings.json", """{ "Routing": { "Period": "01:00:00" } }""");
        directory.Write("routing.json", """{ "Routing": { "RouteIdleTimeout": "02:00:00" } }""");

        var act = () => directory.Migrate();

        act.Should().Throw<ConfigException>().WithMessage("*'Routing'*");
        directory.GetFileNames().Should().Equal("routing.json", "settings.json");
    }

    [Fact]
    public void Migrate_FailsIfOldAndNewNamesAreSet()
    {
        using var directory = new TemporaryConfigDirectory();
        directory.Write("settings.json", """{ "Cache": { "TimeToLive": "00:01:00", "Ttl": "00:02:00" } }""");

        var act = () => directory.Migrate();

        act.Should().Throw<ConfigException>().WithMessage("*'TimeToLive'*'Ttl'*");
    }

    [Fact]
    public void Migrate_MatchesKeysCaseInsensitively()
    {
        using var directory = new TemporaryConfigDirectory();
        directory.Write("settings.json", """
            { "cache": { "timeToLive": "00:01:00" }, "server": { "listenOn": [ { "enableIpV6": true } ] } }
            """);

        directory.Migrate();

        ShouldBeJson(directory, "settings.json", """
            { "cache": { "Ttl": "00:01:00" }, "server": { "listenOn": [ { "EnableIPv6": true } ] } }
            """);
    }

    private static void ShouldBeJson(TemporaryConfigDirectory directory, string fileName, string expectedJson)
    {
        var actual = directory.ReadJson(fileName);
        var expected = JsonNode.Parse(expectedJson);
        var actualJson = actual?.ToJsonString();
        var formattedExpectedJson = expected?.ToJsonString();

        actualJson.Should().Be(formattedExpectedJson);
    }
}
