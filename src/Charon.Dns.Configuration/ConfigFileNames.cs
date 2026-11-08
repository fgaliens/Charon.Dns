namespace Charon.Dns.Configuration;

/// <summary>
/// Names of the configuration files inside a configuration directory.
/// </summary>
public sealed record ConfigFileNames(string Settings, string Zones, string Routing, string Version)
{
    public static ConfigFileNames Production { get; } = new(
        "settings.json",
        "zones.json",
        "routing.json",
        "config.version.json");

    public static ConfigFileNames Debug { get; } = new(
        "settings.debug.json",
        "zones.debug.json",
        "routing.debug.json",
        "config.version.debug.json");
}
