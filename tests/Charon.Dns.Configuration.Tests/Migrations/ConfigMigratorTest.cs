using Charon.Dns.Configuration.Documents;
using Charon.Dns.Configuration.Migrations;
using FluentAssertions;
using JetBrains.Annotations;
using Xunit;

namespace Charon.Dns.Configuration.Tests.Migrations;

[TestSubject(typeof(ConfigMigrator))]
public class ConfigMigratorTest
{
    [Fact]
    public void Migrate_DoesNothingForCurrentVersion()
    {
        using var directory = new TemporaryConfigDirectory();
        directory.Write("settings.json", """{ "LogLevel": "Debug" }""");
        directory.Write("config.version.json", """{ "Version": 1 }""");

        var result = directory.Migrate();

        result.IsMigrated.Should().BeFalse();
        directory.Read("settings.json").Should().Be("""{ "LogLevel": "Debug" }""");
        directory.Read("config.version.json").Should().Be("""{ "Version": 1 }""");
    }

    [Fact]
    public void Migrate_FailsForNewerVersion()
    {
        using var directory = new TemporaryConfigDirectory();
        directory.Write("settings.json", "{}");
        directory.Write("config.version.json", """{ "Version": 100 }""");

        var act = () => directory.Migrate();

        act.Should().Throw<ConfigException>().WithMessage("*version 100*");
    }

    [Fact]
    public void Migrate_DoesNotWriteAnythingOnFailure()
    {
        using var directory = new TemporaryConfigDirectory();
        directory.Write("settings.json", """{ "LogLevel": "Debug", "Routing": [] }""");

        var act = () => directory.Migrate();

        act.Should().Throw<ConfigException>();
        directory.GetFileNames().Should().Equal("settings.json");
    }

    [Fact]
    public void Migrate_WorksWithoutAnyFiles()
    {
        using var directory = new TemporaryConfigDirectory();

        var result = directory.Migrate();

        result.ToVersion.Should().Be(ConfigMigrator.Default.CurrentVersion);
        directory.GetFileNames().Should().Equal("config.version.json");
    }

    [Fact]
    public void Constructor_RequiresSequentialMigrations()
    {
        var migration = new TestMigration(1);

        var act = () => new ConfigMigrator([migration]);

        act.Should().Throw<ArgumentException>();
    }

    private class TestMigration(int fromVersion) : IConfigMigration
    {
        public int FromVersion => fromVersion;

        public void Apply(ConfigWorkspace workspace, ICollection<string> notes)
        {
        }
    }
}
