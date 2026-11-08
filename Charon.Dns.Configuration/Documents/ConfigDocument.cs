using System.Text.Json.Nodes;
using Charon.Dns.Configuration.Json;

namespace Charon.Dns.Configuration.Documents;

/// <summary>
/// A configuration file loaded for editing. The file may not exist yet.
/// </summary>
public sealed class ConfigDocument
{
    private readonly string? _originalJson;

    private ConfigDocument(string path, JsonObject? root)
    {
        Path = path;
        Root = root;
        _originalJson = root?.ToJsonString();
    }

    public string Path { get; }

    public string FileName => System.IO.Path.GetFileName(Path);

    /// <summary>
    /// Content of the file, null if the file does not exist.
    /// </summary>
    public JsonObject? Root { get; private set; }

    public bool ExistsOnDisk => _originalJson is not null;

    public bool IsModified => Root?.ToJsonString() != _originalJson;

    public JsonObject GetOrCreateRoot()
    {
        return Root ??= new JsonObject();
    }

    public static ConfigDocument Load(string path)
    {
        if (!File.Exists(path))
        {
            return new(path, null);
        }

        var json = File.ReadAllText(path);
        var root = JsonFile.Parse(json, path);
        return new(path, root);
    }
}
