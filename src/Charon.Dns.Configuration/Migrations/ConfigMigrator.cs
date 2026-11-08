using Charon.Dns.Configuration.Documents;
using Charon.Dns.Configuration.Migrations.V0ToV1;

namespace Charon.Dns.Configuration.Migrations;

public sealed class ConfigMigrator
{
    private readonly IReadOnlyList<IConfigMigration> _migrations;

    public ConfigMigrator(IReadOnlyList<IConfigMigration> migrations)
    {
        for (var i = 0; i < migrations.Count; i++)
        {
            if (migrations[i].FromVersion != i)
            {
                throw new ArgumentException($"Migration #{i} must convert from version {i}", nameof(migrations));
            }
        }

        _migrations = migrations;
    }

    public static ConfigMigrator Default { get; } = new([
        new V0ToV1Migration(),
    ]);

    public int CurrentVersion => _migrations.Count;

    /// <summary>
    /// Converts the workspace to <see cref="CurrentVersion"/> without saving it.
    /// </summary>
    public MigrationResult Migrate(ConfigWorkspace workspace)
    {
        var fromVersion = workspace.Version;
        if (fromVersion > CurrentVersion)
        {
            throw new ConfigException(
                $"Configuration files have version {fromVersion}, but this build supports up to version {CurrentVersion}. Update Charon.Dns");
        }

        var notes = new List<string>();
        for (var version = fromVersion; version < CurrentVersion; version++)
        {
            _migrations[version].Apply(workspace, notes);
        }

        workspace.Version = CurrentVersion;

        return new()
        {
            FromVersion = fromVersion,
            ToVersion = CurrentVersion,
            Notes = notes,
        };
    }

    /// <summary>
    /// Loads the configuration files of the directory, converts them to <see cref="CurrentVersion"/> and saves them.
    /// </summary>
    public MigrationResult MigrateFiles(string directory, ConfigFileNames fileNames)
    {
        var workspace = ConfigWorkspace.Load(directory, fileNames);
        var result = Migrate(workspace);
        var backupFiles = workspace.Save($"v{result.FromVersion}");

        return result with { BackupFiles = backupFiles };
    }
}
