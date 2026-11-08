using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Charon.Dns.Configuration.Json;

/// <summary>
/// Reads and writes configuration JSON the same way Microsoft.Extensions.Configuration reads it:
/// comments and trailing commas are allowed.
/// </summary>
internal static class JsonFile
{
    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static JsonObject Parse(string json, string path)
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(json, documentOptions: DocumentOptions);
        }
        catch (JsonException e)
        {
            throw new ConfigException($"'{path}' is not a valid JSON file: {e.Message}", e);
        }

        if (node is not JsonObject root)
        {
            throw new ConfigException($"'{path}' must contain a JSON object");
        }

        return root;
    }

    public static string Serialize(JsonObject root)
    {
        return root.ToJsonString(WriteOptions) + Environment.NewLine;
    }

    /// <summary>
    /// Replaces the file in a single step, so a crash never leaves a half-written file.
    /// </summary>
    public static void WriteAtomically(string path, string content)
    {
        var temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, content);
        File.Move(temporaryPath, path, overwrite: true);
    }
}
