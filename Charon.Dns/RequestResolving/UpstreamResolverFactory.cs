using Charon.Dns.Lib.Client.RequestResolver;
using Charon.Dns.Settings;
using Charon.Dns.Utils.Units;
using Serilog;

namespace Charon.Dns.RequestResolving;

public static class UpstreamResolverFactory
{
    public static IRequestResolver Create(
        UpstreamServerSettingsItem server,
        ByteUnit socketBufferSize,
        ILogger logger)
    {
        var endPoint = server.Address.EndPoint;
        switch (server.Mode)
        {
            case TransportMode.UdpAndTcp:
                return new UdpAndTcpRequestResolver(endPoint, socketBufferSize, logger);
            case TransportMode.DoT:
                return new DotRequestResolver(endPoint, server.Host, logger);
            case TransportMode.DoH:
                return new DohRequestResolver(endPoint, server.Host, server.Path, logger);
            default:
                throw new ArgumentOutOfRangeException(nameof(server), server.Mode, "Unknown transport mode");
        }
    }
}
