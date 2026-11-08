using System.Text.Json.Nodes;
using Charon.Dns.Configuration.Documents;
using Charon.Dns.Configuration.Json;

namespace Charon.Dns.Configuration.Migrations.V0ToV1;

internal static class RemoveObsoleteFields
{
    public static void Apply(ConfigWorkspace workspace, ICollection<string> notes)
    {
        var settings = workspace.Settings.Root;
        if (settings is null)
        {
            return;
        }

        RemoveDebugOnlyListeners(settings, notes);

        var upstream = settings.GetObject("Upstream");
        upstream?.RemoveProperty("ResolvingConcurrencyLimit");

        // The version is kept in its own file
        settings.RemoveProperty("Version");
    }

    /// <summary>
    /// 1.x skipped "DebugOnly" listeners in release builds, so they are removed to keep them inactive.
    /// </summary>
    private static void RemoveDebugOnlyListeners(JsonObject settings, ICollection<string> notes)
    {
        var listeners = settings.GetObject("Server")?.GetArray("ListenOn");
        var listenerObjects = listeners.Objects().ToArray();

        foreach (var listener in listenerObjects)
        {
            var debugOnly = listener.GetString("DebugOnly");
            if (!listener.RemoveProperty("DebugOnly")
                || !string.Equals(debugOnly, "true", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var address = listener.GetString("Address");
            var port = listener.GetString("Port");
            listeners!.Remove(listener);
            notes.Add($"Removed listener {address}:{port} marked as 'DebugOnly'");
        }
    }
}
