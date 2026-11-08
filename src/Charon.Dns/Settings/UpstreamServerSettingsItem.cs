using Charon.Dns.Exceptions;
using Charon.Dns.Extensions;
using Microsoft.Extensions.Configuration;

namespace Charon.Dns.Settings;

public record UpstreamServerSettingsItem
{
    private const string DefaultPath = "/dns-query";

    public required DnsServerAddress Address { get; init; }
    public required TransportMode Mode { get; init; }

    /// <summary>
    /// Name to validate the server certificate against (RFC 8310), also sent as SNI and as the HTTP host.
    /// </summary>
    public required string Host { get; init; }

    /// <summary>
    /// Path of the DoH URI template (RFC 8484, 3).
    /// </summary>
    public required string Path { get; init; }

    public static UpstreamServerSettingsItem Initialize(IConfigurationSection section)
    {
        if (section.Value is not null)
        {
            throw new SettingsValidationException(
                $"'{section.Path}' must be an object like {{ \"Address\": \"{section.Value}\" }}. "
                + "Run 'Charon.Dns settings migrate' to convert configuration files");
        }

        var mode = section.GetStrictSectionEnumValue("Mode", TransportMode.UdpAndTcp);
        var rawAddress = section.GetSectionValue("Address");
        if (!DnsServerAddress.TryParse(rawAddress, mode.DefaultPort, out var address))
        {
            throw new SettingsValidationException($"Unable to parse value for key 'Address' in '{section.Path}'");
        }

        var host = section.GetOptionalSectionValue("Host") ?? address.EndPoint.Address.ToString();
        var path = section.GetOptionalSectionValue("Path") ?? DefaultPath;
        if (!path.StartsWith('/'))
        {
            throw new SettingsValidationException($"Value for key 'Path' in '{section.Path}' must start with '/'");
        }

        return new()
        {
            Address = address,
            Mode = mode,
            Host = host,
            Path = path,
        };
    }
}
