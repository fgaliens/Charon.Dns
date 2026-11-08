using Charon.Dns.Configuration.Documents;

namespace Charon.Dns.Configuration.Migrations;

/// <summary>
/// Converts configuration files from <see cref="FromVersion"/> to the next version.
/// </summary>
/// <remarks>
/// A migration must be idempotent: it must not change files that are already in the next format.
/// Files with a missing version file are treated as version 0, though they may already be up to date
/// (e.g. a fresh installation).
/// </remarks>
public interface IConfigMigration
{
    int FromVersion { get; }

    void Apply(ConfigWorkspace workspace, ICollection<string> notes);
}
