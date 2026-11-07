#nullable enable
using System.Net;
using Charon.Dns.Lib.Protocol.ResourceRecords;

namespace Charon.Dns.Lib.Server.Zones
{
    public record ZoneRecord
    {
        public required IResourceRecord Record { get; init; }

        // Stored mapped to IPv6, so it can be compared with any incoming remote address
        public IPAddress? ExpectedRemoteAddress { get; init; }
    }
}
