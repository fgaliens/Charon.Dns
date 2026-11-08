using System.Text.Json;
using System.Text.Json.Nodes;
using Charon.Dns.Configuration.Json;

namespace Charon.Dns.Configuration.Versioning;

/// <summary>
/// The file that stores the format version of the whole set of configuration files.
/// It is kept apart from the settings, so editing the settings never changes the version.
/// </summary>
public static class ConfigVersionFile
{
    private const string VersionKey = "Version";

    /// <summary>
    /// Returns null if the file is missing, broken or has no version.
    /// </summary>
    public static int? TryRead(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(path);
            var root = JsonFile.Parse(json, path);
            var value = root.GetProperty(VersionKey) as JsonValue;
            if (value is not null && value.TryGetValue<int>(out var version) && version >= 0)
            {
                return version;
            }

            return null;
        }
        catch (Exception e) when (e is ConfigException or IOException or JsonException)
        {
            return null;
        }
    }

    public static void Write(string path, int version)
    {
        var content = $$"""
            // Managed by Charon.Dns, do not edit.
            // Format version of the configuration files in this directory.
            {
              "{{VersionKey}}": {{version}}
            }

            """;

        JsonFile.WriteAtomically(path, content);
    }
}
