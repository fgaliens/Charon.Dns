using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Charon.Dns.AccessControl;
using FluentAssertions;
using JetBrains.Annotations;
using Xunit;

namespace Charon.Dns.Tests.AccessControl;

[TestSubject(typeof(UfwStatusParser))]
public class UfwStatusParserTest
{
    private const string Tag = "charon-dns-autoblock";

    [Fact]
    public async Task ExtractBlockedIps_ReturnsEmpty_WhenNoLinesProduced()
    {
        var result = await Collect(UfwStatusParser.ExtractBlockedIps(Lines(), Tag));

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ExtractBlockedIps_ReturnsIp_FromTaggedLine()
    {
        var result = await Collect(UfwStatusParser.ExtractBlockedIps(Lines(
            "Status: active",
            "[ 1] Anywhere on wg0        DENY FWD    10.8.0.2            # charon-dns-autoblock",
            "[ 2] Anywhere                   ALLOW IN    192.168.1.0/24"), Tag));

        result.Should().ContainSingle().Which.Should().Be(IPAddress.Parse("10.8.0.2"));
    }

    [Fact]
    public async Task ExtractBlockedIps_IgnoresLines_WithoutTag()
    {
        var result = await Collect(UfwStatusParser.ExtractBlockedIps(Lines(
            "[ 1] Anywhere on wg0        DENY FWD    10.8.0.2",
            "[ 2] Anywhere                   ALLOW IN    192.168.1.5"), Tag));

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ExtractBlockedIps_ReturnsMultipleIps_FromMultipleTaggedLines()
    {
        var result = await Collect(UfwStatusParser.ExtractBlockedIps(Lines(
            "[ 1] Anywhere on wg0        DENY FWD    10.8.0.2            # charon-dns-autoblock",
            "[ 2] Anywhere on wg0        DENY FWD    10.8.0.3            # charon-dns-autoblock",
            "[ 3] Anywhere                   ALLOW IN    192.168.1.5"), Tag));

        result.Should().BeEquivalentTo(
        [
            IPAddress.Parse("10.8.0.2"),
            IPAddress.Parse("10.8.0.3"),
        ]);
    }

    [Fact]
    public async Task ExtractBlockedIps_IgnoresTaggedLine_WithNoParsableIp()
    {
        var result = await Collect(UfwStatusParser.ExtractBlockedIps(Lines(
            "[ 1] Anywhere on wg0        DENY FWD    # charon-dns-autoblock"), Tag));

        result.Should().BeEmpty();
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
