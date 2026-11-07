using Charon.Dns.Exceptions;
using Charon.Dns.Extensions;
using Charon.Dns.Lib.Protocol;
using Microsoft.Extensions.Configuration;

namespace Charon.Dns.Settings;

public record ZonesSettings : ISettings<ZonesSettings>
{
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(5);

    public required IReadOnlyCollection<ZoneSettings> Zones { get; init; }

    public record ZoneSettings
    {
        public required string Origin { get; init; }
        public required TimeSpan Ttl { get; init; }
        public required SoaSettings Soa { get; init; }
        public required IReadOnlyCollection<RecordSettings> Records { get; init; }
    }

    public record SoaSettings
    {
        public required string PrimaryNameServer { get; init; }
        public required string ResponsibleMailbox { get; init; }
        public required long Serial { get; init; }
        public required TimeSpan Refresh { get; init; }
        public required TimeSpan Retry { get; init; }
        public required TimeSpan Expire { get; init; }
        public required TimeSpan MinimumTtl { get; init; }
    }

    public record RecordSettings
    {
        public required string Name { get; init; }
        public required RecordType Type { get; init; }
        public required string Value { get; init; }
        public required TimeSpan Ttl { get; init; }
        public required string? ResolveOnlyIfRequestCameFrom { get; init; }
    }

    public static ZonesSettings Initialize(IConfiguration config)
    {
        var rawDefaultTtl = config["DefaultTtl"];
        var defaultTtl = TimeSpan.TryParse(rawDefaultTtl, out var parsedDefaultTtl)
            ? parsedDefaultTtl
            : DefaultTtl;

        var zoneSections = config
            .GetSection("Zones")
            .GetChildren();

        var zones = new List<ZoneSettings>();
        foreach (var zoneSection in zoneSections)
        {
            var origin = zoneSection.GetSectionValue("Origin");
            var ttl = zoneSection.GetSectionValue("Ttl", defaultTtl);

            var soaSection = zoneSection.GetSection("Soa");
            var soa = new SoaSettings
            {
                PrimaryNameServer = soaSection.GetOptionalSectionValue("PrimaryNameServer") ?? "ns",
                ResponsibleMailbox = soaSection.GetOptionalSectionValue("ResponsibleMailbox") ?? "hostmaster",
                Serial = soaSection.GetSectionValue("Serial", 1L),
                Refresh = soaSection.GetSectionValue("Refresh", TimeSpan.FromHours(1)),
                Retry = soaSection.GetSectionValue("Retry", TimeSpan.FromMinutes(10)),
                Expire = soaSection.GetSectionValue("Expire", TimeSpan.FromDays(1)),
                MinimumTtl = soaSection.GetSectionValue("MinimumTtl", ttl),
            };

            var recordSections = zoneSection
                .GetSection("Records")
                .GetChildren();

            var records = new List<RecordSettings>();
            foreach (var recordSection in recordSections)
            {
                var record = new RecordSettings
                {
                    Name = recordSection.GetSectionValue("Name"),
                    Type = ParseRecordType(recordSection),
                    Value = recordSection.GetSectionValue("Value"),
                    Ttl = recordSection.GetSectionValue("Ttl", ttl),
                    ResolveOnlyIfRequestCameFrom = recordSection.GetOptionalSectionValue("ResolveOnlyIfRequestCameFrom"),
                };

                records.Add(record);
            }

            var zone = new ZoneSettings
            {
                Origin = origin,
                Ttl = ttl,
                Soa = soa,
                Records = records,
            };

            zones.Add(zone);
        }

        return new ZonesSettings
        {
            Zones = zones,
        };
    }

    private static RecordType ParseRecordType(IConfigurationSection recordSection)
    {
        var rawType = recordSection.GetSectionValue("Type");
        if (!Enum.TryParse<RecordType>(rawType, ignoreCase: true, out var type)
            || !Enum.IsDefined(type))
        {
            throw new SettingsValidationException($"Unknown record type '{rawType}' in '{recordSection.Path}'");
        }

        return type;
    }
}
