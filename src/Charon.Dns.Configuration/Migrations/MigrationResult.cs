namespace Charon.Dns.Configuration.Migrations;

public sealed record MigrationResult
{
    public required int FromVersion { get; init; }
    public required int ToVersion { get; init; }

    /// <summary>
    /// Human-readable description of what has been changed.
    /// </summary>
    public required IReadOnlyList<string> Notes { get; init; }

    public IReadOnlyList<string> BackupFiles { get; init; } = [];

    public bool IsMigrated => FromVersion != ToVersion;
}
