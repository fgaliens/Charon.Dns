using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Charon.Dns.AccessControl;
using Charon.Dns.Extensions;
using FluentAssertions;
using JetBrains.Annotations;
using Xunit;

namespace Charon.Dns.Tests.AccessControl;

[TestSubject(typeof(UfwStatusParser))]
public class UfwStatusParserTest
{
    private const string Tag = "charon-dns-autoblock";

    [Fact]
    public async Task ExtractDeniedNetworks_ReturnsEmpty_WhenNoLinesProduced()
    {
        var result = await Collect(UfwStatusParser.ExtractDeniedNetworks(Lines(), Tag));

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ExtractDeniedNetworks_ReturnsNetwork_FromTaggedLine()
    {
        var result = await Collect(UfwStatusParser.ExtractDeniedNetworks(Lines(
            "Status: active",
            "[ 1] Anywhere on wg0        DENY FWD    10.8.0.2            # charon-dns-autoblock",
            "[ 2] Anywhere                   ALLOW IN    192.168.1.0/24"), Tag));

        result.Should().ContainSingle().Which.Should().Be(IPAddress.Parse("10.8.0.2").ToIPNetwork());
    }

    [Fact]
    public async Task ExtractDeniedNetworks_ParsesCidrNotation_FromTaggedLine()
    {
        var result = await Collect(UfwStatusParser.ExtractDeniedNetworks(Lines(
            "[ 1] Anywhere on wg0        DENY FWD    0.0.0.0/0           # charon-dns-autoblock"), Tag));

        result.Should().ContainSingle().Which.Should().Be(IPNetwork.Parse("0.0.0.0/0"));
    }

    [Fact]
    public async Task ExtractDeniedNetworks_IgnoresLines_WithoutTag()
    {
        var result = await Collect(UfwStatusParser.ExtractDeniedNetworks(Lines(
            "[ 1] Anywhere on wg0        DENY FWD    10.8.0.2",
            "[ 2] Anywhere                   ALLOW IN    192.168.1.5"), Tag));

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ExtractDeniedNetworks_IgnoresTaggedAllowLines()
    {
        var result = await Collect(UfwStatusParser.ExtractDeniedNetworks(Lines(
            "[ 1] Anywhere on wg0        ALLOW FWD    10.8.0.2            # charon-dns-autoblock"), Tag));

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ExtractDeniedNetworks_ReturnsMultipleNetworks_FromMultipleTaggedLines()
    {
        var result = await Collect(UfwStatusParser.ExtractDeniedNetworks(Lines(
            "[ 1] Anywhere on wg0        DENY FWD    10.8.0.2            # charon-dns-autoblock",
            "[ 2] Anywhere on wg0        DENY FWD    10.8.0.3            # charon-dns-autoblock",
            "[ 3] Anywhere                   ALLOW IN    192.168.1.5"), Tag));

        result.Should().BeEquivalentTo(
        [
            IPAddress.Parse("10.8.0.2").ToIPNetwork(),
            IPAddress.Parse("10.8.0.3").ToIPNetwork(),
        ]);
    }

    [Fact]
    public async Task ExtractDeniedNetworks_IgnoresTaggedLine_WithNoParsableIp()
    {
        var result = await Collect(UfwStatusParser.ExtractDeniedNetworks(Lines(
            "[ 1] Anywhere on wg0        DENY FWD    # charon-dns-autoblock"), Tag));

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ExtractAllowedNetworks_ReturnsNetwork_FromTaggedAllowLine()
    {
        var result = await Collect(UfwStatusParser.ExtractAllowedNetworks(Lines(
            "[ 1] Anywhere on wg0        ALLOW FWD    10.8.0.5            # charon-dns-autoblock",
            "[ 2] Anywhere on wg0        DENY FWD    10.8.0.0/24           # charon-dns-autoblock"), Tag));

        result.Should().ContainSingle().Which.Should().Be(IPAddress.Parse("10.8.0.5").ToIPNetwork());
    }

    [Fact]
    public async Task IsActive_ReturnsTrue_WhenStatusLineSaysActive()
    {
        var result = await UfwStatusParser.IsActive(Lines(
            "Status: active",
            "",
            "To                         Action      From"));

        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsActive_ReturnsFalse_WhenStatusLineSaysInactive()
    {
        var result = await UfwStatusParser.IsActive(Lines("Status: inactive"));

        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsActive_ReturnsFalse_WhenNoLinesProduced()
    {
        var result = await UfwStatusParser.IsActive(Lines());

        result.Should().BeFalse();
    }

    private static async IAsyncEnumerable<string> Lines(params string[] lines)
    {
        foreach (var line in lines)
        {
            yield return line;
        }

        await Task.CompletedTask;
    }

    private static async Task<List<T>> Collect<T>(IAsyncEnumerable<T> source)
    {
        var result = new List<T>();
        await foreach (var item in source)
        {
            result.Add(item);
        }

        return result;
    }
}
