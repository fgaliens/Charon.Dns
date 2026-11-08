using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using Charon.Dns.Exceptions;
using Charon.Dns.Settings;
using FluentAssertions;
using JetBrains.Annotations;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Charon.Dns.Tests.Settings;

[TestSubject(typeof(DnsChainSettings))]
public class DnsChainSettingsTest
{
    [Fact]
    public void Initialize_UsesDefaultsOfMode()
    {
        var settings = Parse("""
            {
              "Upstream": {
                "DefaultServers": [
                  { "Address": "77.88.8.8" },
                  { "Address": "1.1.1.1", "Mode": "DoT" },
                  { "Address": "8.8.8.8", "Mode": "doh" }
                ]
              }
            }
            """);

        var servers = settings.DefaultServers.ToArray();
        servers.Select(x => x.Mode).Should().Equal(TransportMode.UdpAndTcp, TransportMode.DoT, TransportMode.DoH);
        servers.Select(x => x.Address.EndPoint.Port).Should().Equal(53, 853, 443);
        servers.Select(x => x.Host).Should().Equal("77.88.8.8", "1.1.1.1", "8.8.8.8");
        servers.Should().OnlyContain(x => x.Path == "/dns-query");
    }

    [Fact]
    public void Initialize_ReadsExplicitValues()
    {
        var settings = Parse("""
            {
              "Upstream": {
                "DefaultServers": [
                  { "Address": "8.8.8.8:8443", "Mode": "DoH", "Host": "dns.google", "Path": "/resolve" }
                ],
                "RoutedServers": [
                  { "Address": "1.1.1.1", "Interface": "wg0", "Mode": "DoT", "Host": "one.one.one.one" }
                ]
              }
            }
            """);

        var defaultServer = settings.DefaultServers.Single();
        defaultServer.Address.EndPoint.Should().Be(new IPEndPoint(IPAddress.Parse("8.8.8.8"), 8443));
        defaultServer.Host.Should().Be("dns.google");
        defaultServer.Path.Should().Be("/resolve");

        var routedServer = settings.RoutedServers.Single();
        routedServer.InterfaceToRouteThrough.Should().Be("wg0");
        routedServer.Upstream.Mode.Should().Be(TransportMode.DoT);
        routedServer.Upstream.Address.EndPoint.Port.Should().Be(853);
        routedServer.Upstream.Host.Should().Be("one.one.one.one");
    }

    [Fact]
    public void Initialize_StringServer_Throws()
    {
        var act = () => Parse("""{ "Upstream": { "DefaultServers": [ "77.88.8.8" ] } }""");

        act.Should().Throw<SettingsValidationException>().WithMessage("*migrate*");
    }

    [Theory]
    [InlineData("Udp")]
    [InlineData("1")]
    [InlineData("")]
    public void Initialize_UnknownMode_Throws(string mode)
    {
        var act = () => Parse($$"""{ "Upstream": { "DefaultServers": [ { "Address": "77.88.8.8", "Mode": "{{mode}}" } ] } }""");

        act.Should().Throw<SettingsValidationException>().WithMessage("*Mode*");
    }

    [Fact]
    public void Initialize_RelativePath_Throws()
    {
        var act = () => Parse("""{ "Upstream": { "DefaultServers": [ { "Address": "8.8.8.8", "Mode": "DoH", "Path": "dns-query" } ] } }""");

        act.Should().Throw<SettingsValidationException>().WithMessage("*Path*");
    }

    private static DnsChainSettings Parse(string json)
    {
        var jsonBytes = Encoding.UTF8.GetBytes(json);
        var jsonStream = new MemoryStream(jsonBytes);
        var config = new ConfigurationBuilder()
            .AddJsonStream(jsonStream)
            .Build();

        return DnsChainSettings.Initialize(config);
    }
}
