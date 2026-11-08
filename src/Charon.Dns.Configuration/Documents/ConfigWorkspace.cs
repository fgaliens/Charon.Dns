using Charon.Dns.Configuration.Json;
using Charon.Dns.Configuration.Versioning;

namespace Charon.Dns.Configuration.Documents;

/// <summary>
/// All configuration files of a directory, loaded for editing and saved together.
/// </summary>
public sealed class ConfigWorkspace
{
    private readonly string _versionPath;
    private readonly int? _storedVersion;

    private ConfigWorkspace(
        ConfigDocument settings,
        ConfigDocument zones,
        ConfigDocument routing,
        string versionPath,
        int? storedVersion)
    {
        Settings = settings;
        Zones = zones;
        Routing = routing;
        _versionPath = versionPath;
        _storedVersion = storedVersion;
        Version = storedVersion ?? 0;
    }

    public ConfigDocument Settings { get; }
    public ConfigDocument Zones { get; }
    public ConfigDocument Routing { get; }

    /// <summary>
    /// Format version of the files. 0 if the version file is missing or has no version.
    /// </summary>
    public int Version { get; set; }

    private IEnumerable<ConfigDocument> Documents => [Zones, Routing, Settings];

    public static ConfigWorkspace Load(string directory, ConfigFileNames fileNames)
    {
        var settingsPath = Path.Combine(directory, fileNames.Settings);
        var zonesPath = Path.Combine(directory, fileNames.Zones);
        var routingPath = Path.Combine(directory, fileNames.Routing);
        var versionPath = Path.Combine(directory, fileNames.Version);

        var settings = ConfigDocument.Load(settingsPath);
        var zones = ConfigDocument.Load(zonesPath);
        var routing = ConfigDocument.Load(routingPath);
        var storedVersion = ConfigVersionFile.TryRead(versionPath);

        return new(settings, zones, routing, versionPath, storedVersion);
    }

    /// <summary>
    /// Writes modified files, backing up the previous content as "&lt;file&gt;.&lt;backupTag&gt;.bak".
    /// The version file is written last, so an interrupted save is repeated on the next run.
    /// </summary>
    /// <returns>Paths of the created backups.</returns>
    public IReadOnlyList<string> Save(string backupTag)
    {
        var modifiedDocuments = Documents
            .Where(x => x.IsModified)
            .ToArray();

        var backups = new List<string>();
        foreach (var document in modifiedDocuments.Where(x => x.ExistsOnDisk))
        {
            var backupPath = GetBackupPath(document.Path, backupTag);
            File.Copy(document.Path, backupPath);
            backups.Add(backupPath);
        }

        foreach (var document in modifiedDocuments)
        {
            var json = JsonFile.Serialize(document.Root!);
            JsonFile.WriteAtomically(document.Path, json);
        }

        if (_storedVersion != Version)
        {
            ConfigVersionFile.Write(_versionPath, Version);
        }

        return backups;
    }

    private static string GetBackupPath(string path, string backupTag)
    {
        var backupPath = $"{path}.{backupTag}.bak";
        if (!File.Exists(backupPath))
        {
            return backupPath;
        }

        var timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
        return $"{path}.{backupTag}.{timestamp}.bak";
    }
}
