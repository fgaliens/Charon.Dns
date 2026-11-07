#nullable enable
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Net;
using Charon.Dns.Lib.Protocol;
using Charon.Dns.Lib.Protocol.ResourceRecords;

namespace Charon.Dns.Lib.Server.Zones
{
    public class ZoneResolverBuilder
    {
        private readonly Dictionary<Domain, (StartOfAuthorityResourceRecord Soa, Dictionary<Domain, DomainRecords> Nodes)> _zones = new();
        private readonly Dictionary<Domain, DomainRecords> _standalone = new();

        public ZoneResolverBuilder AddZone(StartOfAuthorityResourceRecord soa)
        {
            if (_zones.ContainsKey(soa.Name))
            {
                throw new ArgumentException($"Zone '{soa.Name}' is already added");
            }

            var nodes = new Dictionary<Domain, DomainRecords>();
            _zones.Add(soa.Name, (soa, nodes));
            AddTo(nodes, soa, null);

            return this;
        }

        public ZoneResolverBuilder AddRecord(Domain zoneOrigin, IResourceRecord record, IPAddress? expectedRemoteAddress = null)
        {
            if (!_zones.TryGetValue(zoneOrigin, out var zone))
            {
                throw new ArgumentException($"Zone '{zoneOrigin}' is not added");
            }

            if (!IsSameOrSubdomain(record.Name, zoneOrigin))
            {
                throw new ArgumentException($"Record '{record.Name}' is outside of zone '{zoneOrigin}'");
            }

            AddTo(zone.Nodes, record, expectedRemoteAddress);

            return this;
        }

        /// <summary>
        /// Adds a record that does not belong to any zone. Such records are answered non-authoritatively
        /// and a query for a missing type is not answered at all, so it can be resolved further.
        /// </summary>
        public ZoneResolverBuilder AddStandaloneRecord(IResourceRecord record, IPAddress? expectedRemoteAddress = null)
        {
            AddTo(_standalone, record, expectedRemoteAddress);

            return this;
        }

        public ZoneResolver Build()
        {
            var zones = new Dictionary<Domain, Zone>();
            foreach (var (origin, (soa, nodes)) in _zones)
            {
                Validate(nodes);

                var frozenNodes = nodes.ToFrozenDictionary();
                var zone = new Zone(soa, frozenNodes);
                zones.Add(origin, zone);
            }

            Validate(_standalone);

            var frozenZones = zones.ToFrozenDictionary();
            var frozenStandalone = _standalone.ToFrozenDictionary();

            return new ZoneResolver(frozenZones, frozenStandalone);
        }

        private static void AddTo(
            Dictionary<Domain, DomainRecords> nodes,
            IResourceRecord record,
            IPAddress? expectedRemoteAddress)
        {
            if (!nodes.TryGetValue(record.Name, out var records))
            {
                records = new DomainRecords();
                nodes.Add(record.Name, records);
            }

            var zoneRecord = new ZoneRecord
            {
                Record = record,
                ExpectedRemoteAddress = expectedRemoteAddress?.MapToIPv6(),
            };
            records.Add(zoneRecord);
        }

        private static void Validate(Dictionary<Domain, DomainRecords> nodes)
        {
            foreach (var (name, records) in nodes)
            {
                if (records is { HasCanonicalName: true, HasNonCanonicalNameRecords: true })
                {
                    throw new ArgumentException($"CNAME record of '{name}' can not coexist with other records");
                }
            }
        }

        private static bool IsSameOrSubdomain(Domain name, Domain origin)
        {
            var current = name;
            while (true)
            {
                if (current.Equals(origin))
                {
                    return true;
                }

                if (!current.TryGetParent(out current))
                {
                    return false;
                }
            }
        }
    }
}
