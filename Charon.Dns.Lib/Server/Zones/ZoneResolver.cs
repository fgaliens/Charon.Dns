#nullable enable
using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Charon.Dns.Lib.Client.RequestResolver;
using Charon.Dns.Lib.Protocol;
using Charon.Dns.Lib.Protocol.ResourceRecords;
using Charon.Dns.Lib.Tracing;

namespace Charon.Dns.Lib.Server.Zones
{
    /// <summary>
    /// Answers requests from locally defined zones and standalone records.
    /// Names inside a zone are answered authoritatively (including NXDOMAIN / NODATA),
    /// so such requests must not be resolved further.
    /// </summary>
    public class ZoneResolver : IRequestResolver
    {
        private const int MaxCanonicalNameChainLength = 8;

        private readonly FrozenDictionary<Domain, Zone> _zones;
        private readonly FrozenDictionary<Domain, DomainRecords> _standalone;
        private readonly bool _recursionAvailable;

        internal ZoneResolver(
            FrozenDictionary<Domain, Zone> zones,
            FrozenDictionary<Domain, DomainRecords> standalone,
            bool recursionAvailable)
        {
            _zones = zones;
            _standalone = standalone;
            _recursionAvailable = recursionAvailable;
        }

        public Task<IResponse> Resolve(
            IRequest request,
            RequestTrace trace,
            CancellationToken cancellationToken = default)
        {
            var response = Response.FromRequest(request);
            response.RecursionAvailable = _recursionAvailable;
            var remoteAddress = trace.RemoteEndPoint.Address;

            foreach (var question in request.Questions)
            {
                if (TryFindZone(question.Name, out var zone))
                {
                    ResolveAuthoritatively(response, question, zone, remoteAddress);
                    continue;
                }

                if (!_standalone.TryGetValue(question.Name, out var records))
                {
                    response.ResponseCode = ResponseCode.NameError;
                    continue;
                }

                var copiedCount = records.CopyTo(response.AnswerRecords, question.Type, remoteAddress);
                if (copiedCount == 0)
                {
                    response.ResponseCode = ResponseCode.NameError;
                }
            }

            return Task.FromResult<IResponse>(response);
        }

        private void ResolveAuthoritatively(Response response, Question question, Zone zone, IPAddress remoteAddress)
        {
            response.AuthorativeServer = true;

            var name = question.Name;
            for (var i = 0; i <= MaxCanonicalNameChainLength; i++)
            {
                if (!zone.TryGetRecords(name, out var records))
                {
                    response.ResponseCode = ResponseCode.NameError;
                    response.AuthorityRecords.Add(zone.Soa);
                    return;
                }

                var copiedCount = records.CopyTo(response.AnswerRecords, question.Type, remoteAddress);
                if (copiedCount > 0)
                {
                    return;
                }

                var copiedCanonicalNameCount = question.Type != RecordType.CNAME
                    ? records.CopyTo(response.AnswerRecords, RecordType.CNAME, remoteAddress)
                    : 0;
                if (copiedCanonicalNameCount > 0)
                {
                    var canonicalName = (CanonicalNameResourceRecord)response.AnswerRecords[^1];
                    name = canonicalName.CanonicalDomainName;

                    // Target is outside of local zones, the client resolves it by itself
                    if (!TryFindZone(name, out _))
                    {
                        return;
                    }

                    continue;
                }

                // NODATA: the name exists, but has no records of the requested type
                response.AuthorityRecords.Add(zone.Soa);
                return;
            }
        }

        private bool TryFindZone(Domain name, [NotNullWhen(true)] out Zone? zone)
        {
            if (_zones.Count == 0)
            {
                zone = null;
                return false;
            }

            var current = name;
            while (true)
            {
                if (_zones.TryGetValue(current, out zone))
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
