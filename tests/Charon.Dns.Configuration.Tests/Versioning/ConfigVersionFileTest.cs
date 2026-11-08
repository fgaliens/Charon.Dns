using Charon.Dns.Configuration.Versioning;
using FluentAssertions;
using JetBrains.Annotations;
using Xunit;

namespace Charon.Dns.Configuration.Tests.Versioning;

[TestSubject(typeof(ConfigVersionFile))]
public class ConfigVersionFileTest
{
    [Theory]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{ "Version": "one" }""")]
    [InlineData("""{ "Version": -1 }""")]
    public void TryRead_ReturnsNullForInvalidFile(string content)
    {
        using var directory = new TemporaryConfigDirectory();
        directory.Write("config.version.json", content);
        var path = Path.Combine(directory.Path, "config.version.json");

        var version = ConfigVersionFile.TryRead(path);

        version.Should().BeNull();
    }

    [Fact]
    public void TryRead_ReturnsNullForMissingFile()
    {
        using var directory = new TemporaryConfigDirectory();
        var path = Path.Combine(directory.Path, "config.version.json");

        var version = ConfigVersionFile.TryRead(path);

        version.Should().BeNull();
    }

    [Fact]
    public void TryRead_ReadsWrittenVersion()
    {
        using var directory = new TemporaryConfigDirectory();
        var path = Path.Combine(directory.Path, "config.version.json");

        ConfigVersionFile.Write(path, 3);
        var version = ConfigVersionFile.TryRead(path);

        version.Should().Be(3);
        directory.GetFileNames().Should().Equal("config.version.json");
    }
}
