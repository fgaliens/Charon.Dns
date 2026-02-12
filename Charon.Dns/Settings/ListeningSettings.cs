using System.Net;
using Charon.Dns.Extensions;
using Microsoft.Extensions.Configuration;

namespace Charon.Dns.Settings;

public record ListeningSettings : ISettings<ListeningSettings>
{
    public required IReadOnlyCollection<ListeningRecord> Items { get; init; }

    public record ListeningRecord
    {
        public required IPAddress Address { get; init; }
        public required int Port { get; init; }
        public required bool DebugOnly { get; init; }
    }

    public static ListeningSettings Initialize(IConfiguration config)
    {
        var items = config
            .GetSection("Server:ListenOn")
            .GetChildren()
            .Select(x => new ListeningRecord
            {
                Address = x.GetSectionValue<IPAddress>("Address"),
                Port = x.GetSectionValue<int>("Port"),
                DebugOnly = x.GetSectionValue("DebugOnly", false),
            })
            .ToArray();

        return new ListeningSettings
        {
            Items = items,
        };
    }
}