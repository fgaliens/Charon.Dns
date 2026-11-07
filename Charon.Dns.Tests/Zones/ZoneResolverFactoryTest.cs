using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Charon.Dns.Exceptions;
using Charon.Dns.Lib.Protocol;
using Charon.Dns.Lib.Protocol.ResourceRecords;
using Charon.Dns.Lib.Server.Zones;
using Charon.Dns.Lib.Tracing;
using Charon.Dns.Settings;
using Charon.Dns.Zones;
using FluentAssertions;
using JetBrains.Annotations;
using Microsoft.Extensions.Configuration;
using Serilog.Core;
using Xunit;

namespace Charon.Dns.Tests.Zones;

[TestSubject(typeof(ZoneResolverFactory))]
public class ZoneResolverFactoryTest
{
    private static readonly DnsRecordsSettings NoLegacyRecords = new() { ARecords = [] };

    [Theory]
    [InlineData("@", "my.home")]
    [InlineData("nas", "nas.my.home")]
    [InlineData("a.b", "a.b.my.home")]
    [InlineData("example.com.", "example.com")]
    public void ResolveName_HandlesOriginRelativeAndAbsoluteNames(string name, string expected)
    {
        var origin = new Domain("my.home");

        var result = ZoneResolverFactory.ResolveName(name, origin);

        result.ToString().Should().Be(expected);
    }

    [Fact]
    public void Settings_UseZoneAndDefaultTtl()
    {
        var settings = Parse("""
            {
              "DefaultTtl": "00:01:00",
              "Zones": [
                  {
                    "Origin": "my.home",
                    "Ttl": "00:02:00",
                    "Records": [
                      { "Name": "@", "Type": "a", "Value": "10.0.0.1" },
                      { "Name": "@", "Type": "TXT", "Value": "x", "Ttl": "00:03:00" }
                    ]
                  },
                  {
                    "Origin": "other.home",
                    "Records": [ { "Name": "@", "Type": "A", "Value": "10.0.0.2" } ]
                  }
              ]
            }
            """);

        var zones = settings.Zones.ToArray();
        var firstZoneTtls = zones[0].Records.Select(record => record.Ttl);
        firstZoneTtls.Should().Equal(TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(3));
        zones[0].Records.First().Type.Should().Be(RecordType.A);
        zones[1].Records.Single().Ttl.Should().Be(TimeSpan.FromMinutes(1));
        zones[1].Soa.MinimumTtl.Should().Be(TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void Settings_UnknownType_Throws()
    {
        var act = () => Parse("""
            { "Zones": [ { "Origin": "my.home", "Records": [ { "Name": "@", "Type": "XYZ", "Value": "1" } ] } ] }
            """);

        act.Should().Throw<SettingsValidationException>();
    }

    [Fact]
    public async Task Create_ParsesAllSupportedRecordTypes()
    {
        var resolver = CreateResolver("""
            {
              "Zones": [
                  {
                    "Origin": "my.home.",
                    "Records": [
                      { "Name": "@", "Type": "A", "Value": "10.0.0.1" },
                      { "Name": "@", "Type": "AAAA", "Value": "fd00::1" },
                      { "Name": "@", "Type": "MX", "Value": "10 mail" },
                      { "Name": "@", "Type": "TXT", "Value": "v=spf1 -all" },
                      { "Name": "@", "Type": "NS", "Value": "ns" },
                      { "Name": "nas", "Type": "CNAME", "Value": "@" },
                      { "Name": "_http._tcp", "Type": "SRV", "Value": "0 5 80 nas" }
                    ]
                  }
              ]
            }
            """);

        var mxResponse = await Resolve(resolver, "my.home", RecordType.MX);
        var mx = (MailExchangeResourceRecord)mxResponse.AnswerRecords.Single();
        mx.Preference.Should().Be(10);
        mx.ExchangeDomainName.ToString().Should().Be("mail.my.home");

        var srvResponse = await Resolve(resolver, "_http._tcp.my.home", RecordType.SRV);
        var srv = (ServiceResourceRecord)srvResponse.AnswerRecords.Single();
        srv.Port.Should().Be(80);
        srv.Weight.Should().Be(5);
        srv.Target.ToString().Should().Be("nas.my.home");

        var anyResponse = await Resolve(resolver, "my.home", RecordType.ANY);
        var anyTypes = anyResponse.AnswerRecords.Select(record => record.Type);
        anyTypes.Should().BeEquivalentTo(
            [RecordType.SOA, RecordType.A, RecordType.AAAA, RecordType.MX, RecordType.TXT, RecordType.NS]);
    }

    [Theory]
    [InlineData("A", "fd00::1")]
    [InlineData("AAAA", "10.0.0.1")]
    [InlineData("MX", "mail")]
    [InlineData("SRV", "0 5 port nas")]
    public void Create_InvalidValue_Throws(string type, string value)
    {
        var act = () => CreateResolver($$"""
            { "Zones": [ { "Origin": "my.home", "Records": [ { "Name": "@", "Type": "{{type}}", "Value": "{{value}}" } ] } ] }
            """);

        act.Should().Throw<SettingsValidationException>();
    }

    [Fact]
    public void Create_CanonicalNameWithOtherRecords_Throws()
    {
        var act = () => CreateResolver("""
            {
              "Zones": [
                  {
                    "Origin": "my.home",
                    "Records": [
                      { "Name": "nas", "Type": "CNAME", "Value": "@" },
                      { "Name": "nas", "Type": "A", "Value": "10.0.0.1" }
                    ]
                  }
              ]
            }
            """);

        act.Should().Throw<SettingsValidationException>();
    }

    [Fact]
    public async Task Create_AddsLegacyRecordsAsStandalone()
    {
        var legacyRecords = new DnsRecordsSettings
        {
            ARecords =
            [
                new() { Name = "my.hm", Address = "10.0.0.1", ResolveOnlyIfRequestCameFrom = null },
            ],
        };
        var zonesSettings = Parse("{}");
        var resolver = ZoneResolverFactory.Create(zonesSettings, legacyRecords, TimeSpan.FromMinutes(1), Logger.None);

        var response = await Resolve(resolver, "my.hm", RecordType.A);

        response.AuthorativeServer.Should().BeFalse();
        var answer = (IpAddressResourceRecord)response.AnswerRecords.Single();
        answer.IpAddress.Should().Be(IPAddress.Parse("10.0.0.1"));
    }

    private static ZonesSettings Parse(string json)
    {
        var jsonBytes = Encoding.UTF8.GetBytes(json);
        var jsonStream = new MemoryStream(jsonBytes);
        var config = new ConfigurationBuilder()
            .AddJsonStream(jsonStream)
            .Build();

        return ZonesSettings.Initialize(config);
    }

    private static ZoneResolver CreateResolver(string json)
    {
        var zonesSettings = Parse(json);
        return ZoneResolverFactory.Create(zonesSettings, NoLegacyRecords, TimeSpan.Zero, Logger.None);
    }

    private static Task<IResponse> Resolve(ZoneResolver resolver, string domain, RecordType recordType)
    {
        var name = new Domain(domain);
        var question = new Question(name, recordType);
        var request = new Request
        {
            Id = 1,
            OperationCode = OperationCode.Query,
        };
        request.Questions.Add(question);

        return resolver.Resolve(request, RequestTrace.Empty);
    }
}
