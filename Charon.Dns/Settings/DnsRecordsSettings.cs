using Microsoft.Extensions.Configuration;

namespace Charon.Dns.Settings
{
    public record DnsRecordsSettings : ISettings<DnsRecordsSettings>
    {
        public required IReadOnlyCollection<ARecord> ARecords { get; init; }
    
        public record ARecord
        {
            public required string Name { get; init; }
            public required string Address { get; init; }
        }
    
        public static DnsRecordsSettings Initialize(IConfiguration config)
        {
            var dnsRecords = config
                .GetSection("Server:DnsRecords");
            
            var aRecords = dnsRecords
                .GetSection("A")
                .GetChildren()
                .Select(x => new ARecord
                {
                    Name = x["Name"]!,
                    Address = x["Address"]!,
                })
                .ToArray();

            return new()
            {
                ARecords = aRecords,
            };
        }
    }
}
