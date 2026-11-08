using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Charon.Dns.Exceptions;
using Charon.Dns.Extensions;
using Charon.Dns.Utils.Units;
using Microsoft.Extensions.Configuration;

namespace Charon.Dns.Settings;

public record ListeningSettings : ISettings<ListeningSettings>
{
    private const string DefaultDohPath = "/dns-query";

    public required int MaxParallelRequestCount { get; init; }
    public required ByteUnit SocketBufferSize { get; init; }
    public required IReadOnlyCollection<ListeningRecord> Items { get; init; }

    public record ListeningRecord
    {
        public required IPAddress Address { get; init; }
        public required int Port { get; init; }
        public required bool EnableIpV6 { get; init; }
        public required TransportMode Mode { get; init; }

        /// <summary>
        /// Server certificate, set for DoT and DoH.
        /// </summary>
        public X509Certificate2? Certificate { get; init; }

        /// <summary>
        /// Path of the DoH URI template (RFC 8484, 3).
        /// </summary>
        public required string Path { get; init; }
    }

    public static ListeningSettings Initialize(IConfiguration config)
    {
        var serverSection = config.GetSection("Server");
        var maxParallelRequestCount = serverSection
            .GetSectionValue("SocketsPerEndpoint", 8)
            .RestrictNotLessThen(1);
        var socketBufferSize = serverSection
            .GetSectionValue("SocketBufferSize", new ByteUnit(1024 * 1024));
        var listeningParams = serverSection
            .GetSection("ListenOn")
            .GetChildren()
            .Select(InitializeRecord)
            .ToArray();

        return new ListeningSettings
        {
            MaxParallelRequestCount = maxParallelRequestCount,
            SocketBufferSize = socketBufferSize,
            Items = listeningParams,
        };
    }

    private static ListeningRecord InitializeRecord(IConfigurationSection section)
    {
        var mode = section.GetStrictSectionEnumValue("Mode", TransportMode.UdpAndTcp);
        var certificate = mode == TransportMode.UdpAndTcp
            ? null
            : LoadCertificate(section);
        var path = section.GetOptionalSectionValue("Path") ?? DefaultDohPath;
        if (!path.StartsWith('/'))
        {
            throw new SettingsValidationException($"Value for key 'Path' in '{section.Path}' must start with '/'");
        }

        return new ListeningRecord
        {
            Address = section.GetSectionValue<IPAddress>("Address"),
            Port = section.GetSectionValue("Port", mode.DefaultPort),
            EnableIpV6 = section.GetSectionValue("EnableIPv6", false),
            Mode = mode,
            Certificate = certificate,
            Path = path,
        };
    }

    /// <summary>
    /// A PKCS#12 file (.pfx, .p12) with an optional password, or a PEM certificate with a PEM key.
    /// </summary>
    private static X509Certificate2 LoadCertificate(IConfigurationSection listenerSection)
    {
        var section = listenerSection.GetSection("Certificate");
        if (!section.Exists())
        {
            throw new SettingsValidationException($"'Certificate' is required for DoT and DoH in '{listenerSection.Path}'");
        }

        var path = section.GetSectionValue("Path");
        var extension = Path.GetExtension(path);
        var isPkcs12 = extension.Equals(".pfx", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".p12", StringComparison.OrdinalIgnoreCase);

        try
        {
            if (isPkcs12)
            {
                var password = section.GetOptionalSectionValue("Password");
                return X509CertificateLoader.LoadPkcs12FromFile(path, password);
            }

            var keyPath = section.GetOptionalSectionValue("KeyPath");
            return X509Certificate2.CreateFromPemFile(path, keyPath);
        }
        catch (Exception e) when (e is CryptographicException or IOException)
        {
            throw new SettingsValidationException($"Unable to load certificate '{path}' in '{section.Path}': {e.Message}");
        }
    }
}
