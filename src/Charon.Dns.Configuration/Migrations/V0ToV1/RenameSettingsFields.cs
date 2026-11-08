using System.Text.Json.Nodes;
using Charon.Dns.Configuration.Documents;
using Charon.Dns.Configuration.Json;

namespace Charon.Dns.Configuration.Migrations.V0ToV1;

internal static class RenameSettingsFields
{
    public static void Apply(ConfigWorkspace workspace)
    {
        var settings = workspace.Settings.Root;
        if (settings is null)
        {
            return;
        }

        MoveLogLevels(settings);

        var server = settings.GetObject("Server");
        if (server is not null)
        {
            server.RenameProperty("MaxParallelRequestCount", "SocketsPerEndpoint");

            var listeners = server.GetArray("ListenOn");
            foreach (var listener in listeners.Objects())
            {
                listener.RenameProperty("EnableIpV6", "EnableIPv6");
            }

            // "Upstream" goes right after "Server"
            settings.TryGetPropertyIgnoreCase("Server", out var serverName, out _);
            var upstreamIndex = settings.IndexOf(serverName!) + 1;
            server.MoveProperty("DnsChain", settings, "Upstream", upstreamIndex);
        }

        var upstream = settings.GetObject("Upstream");
        if (upstream is not null)
        {
            upstream.RenameProperty("SecuredServers", "RoutedServers");

            var routedServers = upstream.GetArray("RoutedServers");
            foreach (var routedServer in routedServers.Objects())
            {
                routedServer.RenameProperty("Ip", "Address");
                routedServer.RenameProperty("RouteThroughInterface", "Interface");
            }
        }

        var cache = settings.GetObject("Cache");
        cache?.RenameProperty("TimeToLive", "Ttl");

        var userAccessControl = settings.GetObject("UserAccessControl");
        userAccessControl?.RenameProperty("ControlledIps", "ControlledSubnets");
    }

    /// <summary>
    /// "LogLevel" and "FileLogLevel" become "Logging:Console" and "Logging:File".
    /// </summary>
    private static void MoveLogLevels(JsonObject settings)
    {
        var hasLogLevel = settings.TryGetPropertyIgnoreCase("LogLevel", out var logLevelName, out _);
        var hasFileLogLevel = settings.TryGetPropertyIgnoreCase("FileLogLevel", out var fileLogLevelName, out _);
        if (!hasLogLevel && !hasFileLogLevel)
        {
            return;
        }

        var logging = settings.GetObject("Logging");
        if (logging is null)
        {
            var firstName = hasLogLevel ? logLevelName! : fileLogLevelName!;
            var loggingIndex = settings.IndexOf(firstName);
            logging = new JsonObject();
            settings.Insert(loggingIndex, "Logging", logging);
        }

        settings.MoveProperty("LogLevel", logging, "Console");
        settings.MoveProperty("FileLogLevel", logging, "File");
    }
}
