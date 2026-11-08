using System.Text.Json;
using System.Text.Json.Nodes;
using Charon.Dns.Configuration.Documents;
using Charon.Dns.Configuration.Json;

namespace Charon.Dns.Configuration.Migrations.V0ToV1;

/// <summary>
/// 2.0 selects a transport (UDP and TCP, DoT, DoH) for every listener and upstream server.
/// 1.x used plain DNS only, so "UdpAndTcp" is set explicitly, and default servers given as strings become objects.
/// </summary>
internal static class AddTransportModes
{
    private const string ModeName = "Mode";
    private const string DefaultMode = "UdpAndTcp";

    public static void Apply(ConfigWorkspace workspace, ICollection<string> notes)
    {
        var settings = workspace.Settings.Root;
        if (settings is null)
        {
            return;
        }

        var listeners = settings.GetObject("Server")?.GetArray("ListenOn");
        var isListenerModeAdded = false;
        foreach (var listener in listeners.Objects())
        {
            isListenerModeAdded |= AddMode(listener, "EnableIPv6");
        }

        if (isListenerModeAdded)
        {
            notes.Add($"Listeners now also accept DNS over TCP on the same port (Mode '{DefaultMode}')");
        }

        var upstream = settings.GetObject("Upstream");
        if (upstream is null)
        {
            return;
        }

        var defaultServers = upstream.GetArray("DefaultServers");
        ConvertDefaultServers(defaultServers);

        var routedServers = upstream.GetArray("RoutedServers");
        foreach (var routedServer in routedServers.Objects())
        {
            AddMode(routedServer, "Interface");
        }
    }

    private static void ConvertDefaultServers(JsonArray? defaultServers)
    {
        if (defaultServers is null)
        {
            return;
        }

        for (var i = 0; i < defaultServers.Count; i++)
        {
            switch (defaultServers[i])
            {
                case JsonObject defaultServer:
                    AddMode(defaultServer, "Address");
                    break;
                case JsonValue value when value.GetValueKind() == JsonValueKind.String:
                    var address = value.GetValue<string>();
                    defaultServers[i] = new JsonObject
                    {
                        ["Address"] = address,
                        [ModeName] = DefaultMode,
                    };
                    break;
                case var node:
                    var path = node?.GetPath() ?? defaultServers.GetPath();
                    throw new ConfigException($"'{path}' must be a JSON object");
            }
        }
    }

    /// <summary>
    /// Adds the default mode right after <paramref name="previousName"/>, or to the end if it is missing.
    /// </summary>
    private static bool AddMode(JsonObject jsonObject, string previousName)
    {
        if (jsonObject.HasProperty(ModeName))
        {
            return false;
        }

        if (jsonObject.TryGetPropertyIgnoreCase(previousName, out var actualName, out _))
        {
            var index = jsonObject.IndexOf(actualName) + 1;
            jsonObject.Insert(index, ModeName, DefaultMode);
        }
        else
        {
            jsonObject.Add(ModeName, DefaultMode);
        }

        return true;
    }
}
