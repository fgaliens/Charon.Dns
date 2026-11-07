#nullable enable
using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using Charon.Dns.Lib.Protocol;
using Charon.Dns.Lib.Protocol.ResourceRecords;

namespace Charon.Dns.Lib.Server.Zones
{
    /// <summary>
    /// A zone the server is authoritative for.
    /// </summary>
    public class Zone
    {
        private readonly FrozenDictionary<Domain, DomainRecords> _nodes;

        internal Zone(StartOfAuthorityResourceRecord soa, FrozenDictionary<Domain, DomainRecords> nodes)
        {
            Soa = soa;
            _nodes = nodes;
        }

        public Domain Origin => Soa.Name;

        public StartOfAuthorityResourceRecord Soa { get; }

        public bool TryGetRecords(Domain name, [NotNullWhen(true)] out DomainRecords? records)
        {
            return _nodes.TryGetValue(name, out records);
        }
    }
}
