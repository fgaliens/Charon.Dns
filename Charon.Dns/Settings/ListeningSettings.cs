using System.Net;
using Charon.Dns.Extensions;
using Charon.Dns.Utils.Units;
using Microsoft.Extensions.Configuration;

namespace Charon.Dns.Settings;

public record ListeningSettings : ISettings<ListeningSettings>
{
    public required int MaxParallelRequestCount { get; init; }
    public required ByteUnit SocketBufferSize { get; init; }
    public required IReadOnlyCollection<ListeningRecord> Items { get; init; }

    public record ListeningRecord
    {
        public required IPAddress Address { get; init; }
        public required int Port { get; init; }
        public required bool EnableIpV6 { get; init; }
        public required bool DebugOnly { get; init; }
    }

    public static ListeningSettings Initialize(IConfiguration config)
    {
        var serverSection = config.GetSection("Server");
        var maxParallelRequestCount = serverSection
            .GetSectionValue("MaxParallelRequestCount", 8)
            .RestrictNotLessThen(1);
        var socketBufferSize = serverSection
            .GetSectionValue("SocketBufferSize", new ByteUnit(1024 * 1024));
        var listeningParams = serverSection
            .GetSection("ListenOn")
            .GetChildren()
            .Select(x => new ListeningRecord
            {
                Address = x.GetSectionValue<IPAddress>("Address"),
                Port = x.GetSectionValue<int>("Port"),
                EnableIpV6 = x.GetSectionValue("EnableIpV6", false),
                DebugOnly = x.GetSectionValue("DebugOnly", false),
            })
            .ToArray();

        return new ListeningSettings
        {
            MaxParallelRequestCount = maxParallelRequestCount,
            SocketBufferSize = socketBufferSize,
            Items = listeningParams,
        };
    }
}