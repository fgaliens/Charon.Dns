using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Charon.Dns.Exceptions;
using Charon.Dns.Settings;
using FluentAssertions;
using JetBrains.Annotations;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Charon.Dns.Tests.Settings;

[TestSubject(typeof(ListeningSettings))]
public class ListeningSettingsTest : IDisposable
{
    private readonly string _directory;

    public ListeningSettingsTest()
    {
        var name = Guid.NewGuid().ToString("N");
        _directory = Path.Combine(Path.GetTempPath(), "charon-dns-tests", name);
        Directory.CreateDirectory(_directory);
    }

    [Fact]
    public void Initialize_UsesUdpAndTcpByDefault()
    {
        var settings = Parse("""{ "Server": { "ListenOn": [ { "Address": "10.0.0.53" } ] } }""");

        var listener = settings.Items.Single();
        listener.Mode.Should().Be(TransportMode.UdpAndTcp);
        listener.Port.Should().Be(53);
        listener.Certificate.Should().BeNull();
    }

    [Fact]
    public void Initialize_LoadsPemCertificate()
    {
        var (certificatePath, keyPath) = WritePemCertificate();

        var settings = Parse($$"""
            {
              "Server": {
                "ListenOn": [
                  { "Address": "10.0.0.53", "Mode": "DoT", "Certificate": { "Path": "{{certificatePath}}", "KeyPath": "{{keyPath}}" } }
                ]
              }
            }
            """);

        var listener = settings.Items.Single();
        listener.Port.Should().Be(853);
        listener.Certificate!.HasPrivateKey.Should().BeTrue();
    }

    [Fact]
    public void Initialize_LoadsPkcs12Certificate()
    {
        var certificatePath = WritePkcs12Certificate("secret");

        var settings = Parse($$"""
            {
              "Server": {
                "ListenOn": [
                  {
                    "Address": "10.0.0.53",
                    "Mode": "DoH",
                    "Path": "/query",
                    "Certificate": { "Path": "{{certificatePath}}", "Password": "secret" }
                  }
                ]
              }
            }
            """);

        var listener = settings.Items.Single();
        listener.Port.Should().Be(443);
        listener.Path.Should().Be("/query");
        listener.Certificate!.HasPrivateKey.Should().BeTrue();
    }

    [Theory]
    [InlineData("DoT")]
    [InlineData("DoH")]
    public void Initialize_MissingCertificate_Throws(string mode)
    {
        var act = () => Parse($$"""{ "Server": { "ListenOn": [ { "Address": "10.0.0.53", "Mode": "{{mode}}" } ] } }""");

        act.Should().Throw<SettingsValidationException>().WithMessage("*Certificate*");
    }

    [Fact]
    public void Initialize_MissingCertificateFile_Throws()
    {
        var certificatePath = EscapeJson(Path.Combine(_directory, "missing.pem"));

        var act = () => Parse($$"""
            { "Server": { "ListenOn": [ { "Address": "10.0.0.53", "Mode": "DoT", "Certificate": { "Path": "{{certificatePath}}" } } ] } }
            """);

        act.Should().Throw<SettingsValidationException>().WithMessage("*missing.pem*");
    }

    [Fact]
    public void Initialize_UnknownMode_Throws()
    {
        var act = () => Parse("""{ "Server": { "ListenOn": [ { "Address": "10.0.0.53", "Mode": "Tcp" } ] } }""");

        act.Should().Throw<SettingsValidationException>().WithMessage("*Mode*");
    }

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
    }

    private static X509Certificate2 CreateCertificate()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var notBefore = DateTimeOffset.UtcNow.AddDays(-1);
        var notAfter = DateTimeOffset.UtcNow.AddDays(1);
        return request.CreateSelfSigned(notBefore, notAfter);
    }

    private (string CertificatePath, string KeyPath) WritePemCertificate()
    {
        using var certificate = CreateCertificate();
        using var key = certificate.GetRSAPrivateKey()!;
        var certificatePath = Path.Combine(_directory, "cert.pem");
        var keyPath = Path.Combine(_directory, "key.pem");
        var certificatePem = certificate.ExportCertificatePem();
        var keyPem = key.ExportPkcs8PrivateKeyPem();
        File.WriteAllText(certificatePath, certificatePem);
        File.WriteAllText(keyPath, keyPem);

        return (EscapeJson(certificatePath), EscapeJson(keyPath));
    }

    private string WritePkcs12Certificate(string password)
    {
        using var certificate = CreateCertificate();
        var certificatePath = Path.Combine(_directory, "cert.pfx");
        var pkcs12 = certificate.Export(X509ContentType.Pkcs12, password);
        File.WriteAllBytes(certificatePath, pkcs12);

        return EscapeJson(certificatePath);
    }

    private static string EscapeJson(string value)
    {
        return value.Replace("\\", "\\\\");
    }

    private static ListeningSettings Parse(string json)
    {
        var jsonBytes = Encoding.UTF8.GetBytes(json);
        var jsonStream = new MemoryStream(jsonBytes);
        var config = new ConfigurationBuilder()
            .AddJsonStream(jsonStream)
            .Build();

        return ListeningSettings.Initialize(config);
    }
}
