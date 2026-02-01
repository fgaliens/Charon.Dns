using System.Net;
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
                Address = IPAddress.Parse(x["Address"]!),
                Port = int.Parse(x["Port"]!),
                DebugOnly = bool.TryParse(x["DebugOnly"], out bool debugOnly) && debugOnly,
            })
            .ToArray();

        return new ListeningSettings
        {
            Items = items,
        };
    }
}