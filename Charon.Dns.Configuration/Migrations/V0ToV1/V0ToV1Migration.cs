using Charon.Dns.Configuration.Documents;

namespace Charon.Dns.Configuration.Migrations.V0ToV1;

/// <summary>
/// Converts the 1.x configuration to the 2.0 format.
/// </summary>
public sealed class V0ToV1Migration : IConfigMigration
{
    public int FromVersion => 0;

    public void Apply(ConfigWorkspace workspace, ICollection<string> notes)
    {
        // Records are moved first: they need "Cache:TimeToLive" before it is renamed
        MoveLegacyRecordsToZones.Apply(workspace, notes);
        MoveRoutingToRoutingFile.Apply(workspace, notes);
        RenameSettingsFields.Apply(workspace);
        RemoveObsoleteFields.Apply(workspace, notes);
        // Modes are added last: they are placed next to renamed fields, and removed listeners don't need them
        AddTransportModes.Apply(workspace, notes);
    }
}
