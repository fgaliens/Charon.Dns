using System.Text.Json.Nodes;
using Charon.Dns.Configuration.Documents;
using Charon.Dns.Configuration.Migrations;

namespace Charon.Dns.Configuration.Tests;

internal sealed class TemporaryConfigDirectory : IDisposable
{
    public static readonly ConfigFileNames FileNames = ConfigFileNames.Production;

    public TemporaryConfigDirectory()
    {
        var name = Guid.NewGuid().ToString("N");
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "charon-dns-config-tests", name);
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Write(string fileName, string content)
    {
        var filePath = GetPath(fileName);
        File.WriteAllText(filePath, content);
    }

    public string Read(string fileName)
    {
        var filePath = GetPath(fileName);
        return File.ReadAllText(filePath);
    }

    public bool Exists(string fileName)
    {
        var filePath = GetPath(fileName);
        return File.Exists(filePath);
    }

    public string[] GetFileNames()
    {
        return Directory.GetFiles(Path)
            .Select(System.IO.Path.GetFileName)
            .OfType<string>()
            .Order()
            .ToArray();
    }

    public JsonNode? ReadJson(string fileName)
    {
        var content = Read(fileName);
        return JsonNode.Parse(content, documentOptions: new() { CommentHandling = System.Text.Json.JsonCommentHandling.Skip });
    }

    public ConfigWorkspace LoadWorkspace()
    {
        return ConfigWorkspace.Load(Path, FileNames);
    }

    public MigrationResult Migrate()
    {
        return ConfigMigrator.Default.MigrateFiles(Path, FileNames);
    }

    public void Dispose()
    {
        Directory.Delete(Path, recursive: true);
    }

    private string GetPath(string fileName)
    {
        return System.IO.Path.Combine(Path, fileName);
    }
}
