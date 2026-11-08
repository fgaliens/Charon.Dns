using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Charon.Dns.Configuration.Json;

/// <summary>
/// Case-insensitive property access, as Microsoft.Extensions.Configuration treats keys case-insensitively.
/// Operations keep the position of properties, so edited files stay close to what the user wrote.
/// </summary>
public static class JsonObjectExtensions
{
    public static bool TryGetPropertyIgnoreCase(
        this JsonObject jsonObject,
        string name,
        [NotNullWhen(true)] out string? actualName,
        out JsonNode? value)
    {
        foreach (var (key, node) in jsonObject)
        {
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
            {
                actualName = key;
                value = node;
                return true;
            }
        }

        actualName = null;
        value = null;
        return false;
    }

    public static bool HasProperty(this JsonObject jsonObject, string name)
    {
        return jsonObject.TryGetPropertyIgnoreCase(name, out _, out _);
    }

    public static JsonNode? GetProperty(this JsonObject jsonObject, string name)
    {
        jsonObject.TryGetPropertyIgnoreCase(name, out _, out var value);
        return value;
    }

    public static JsonObject? GetObject(this JsonObject jsonObject, string name)
    {
        return jsonObject.GetProperty(name) as JsonObject;
    }

    public static JsonArray? GetArray(this JsonObject jsonObject, string name)
    {
        return jsonObject.GetProperty(name) as JsonArray;
    }

    public static string? GetString(this JsonObject jsonObject, string name)
    {
        var value = jsonObject.GetProperty(name) as JsonValue;
        if (value is null)
        {
            return null;
        }

        // Microsoft.Extensions.Configuration reads numbers and booleans as strings too
        return value.GetValueKind() == JsonValueKind.String
            ? value.GetValue<string>()
            : value.ToJsonString();
    }

    public static JsonObject GetOrCreateObject(this JsonObject jsonObject, string name)
    {
        if (jsonObject.TryGetPropertyIgnoreCase(name, out _, out var value))
        {
            return value as JsonObject
                ?? throw new ConfigException($"'{value?.GetPath() ?? name}' must be a JSON object");
        }

        var created = new JsonObject();
        jsonObject.Add(name, created);
        return created;
    }

    public static JsonArray GetOrCreateArray(this JsonObject jsonObject, string name)
    {
        if (jsonObject.TryGetPropertyIgnoreCase(name, out _, out var value))
        {
            return value as JsonArray
                ?? throw new ConfigException($"'{value?.GetPath() ?? name}' must be a JSON array");
        }

        var created = new JsonArray();
        jsonObject.Add(name, created);
        return created;
    }

    /// <summary>
    /// Removes the property and returns its value detached from the object, so it can be added elsewhere.
    /// </summary>
    public static bool TryDetachProperty(this JsonObject jsonObject, string name, out JsonNode? value)
    {
        if (!jsonObject.TryGetPropertyIgnoreCase(name, out var actualName, out value))
        {
            return false;
        }

        jsonObject.Remove(actualName);
        return true;
    }

    public static bool RemoveProperty(this JsonObject jsonObject, string name)
    {
        return jsonObject.TryDetachProperty(name, out _);
    }

    /// <summary>
    /// Renames the property in place. Does nothing if the property is missing or already has the new name.
    /// </summary>
    public static bool RenameProperty(this JsonObject jsonObject, string name, string newName)
    {
        if (!jsonObject.TryGetPropertyIgnoreCase(name, out var actualName, out var value)
            || actualName == newName)
        {
            return false;
        }

        if (jsonObject.TryGetPropertyIgnoreCase(newName, out var existingName, out _)
            && existingName != actualName)
        {
            var path = jsonObject.GetPath();
            throw new ConfigException($"Both '{actualName}' and '{existingName}' are set in '{path}', keep only '{newName}'");
        }

        var index = jsonObject.IndexOf(actualName);
        jsonObject.RemoveAt(index);
        jsonObject.Insert(index, newName, value);
        return true;
    }

    /// <summary>
    /// Moves the property to <paramref name="target"/> under <paramref name="newName"/>.
    /// When <paramref name="index"/> is set, the property is inserted at that position.
    /// </summary>
    public static bool MoveProperty(
        this JsonObject jsonObject,
        string name,
        JsonObject target,
        string newName,
        int? index = null)
    {
        if (!jsonObject.HasProperty(name))
        {
            return false;
        }

        if (target.TryGetPropertyIgnoreCase(newName, out var existingName, out _))
        {
            var sourcePath = jsonObject.GetPath();
            var targetPath = target.GetPath();
            throw new ConfigException(
                $"'{name}' in '{sourcePath}' can't be moved: '{existingName}' is already set in '{targetPath}'");
        }

        jsonObject.TryDetachProperty(name, out var value);
        if (index is { } position)
        {
            target.Insert(position, newName, value);
        }
        else
        {
            target.Add(newName, value);
        }

        return true;
    }

    public static IEnumerable<JsonObject> Objects(this JsonArray? jsonArray)
    {
        return jsonArray?.OfType<JsonObject>() ?? [];
    }
}
