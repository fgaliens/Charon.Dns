#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Charon.Dns.Lib.Client.RequestResolver;
using Charon.Dns.Lib.Protocol;
using Charon.Dns.Lib.Protocol.ResourceRecords;

namespace Charon.Dns.Lib.Server
{
    public class MasterFile : IRequestResolver
    {
        private static readonly TimeSpan DefaultTtl = new TimeSpan(0);
        
        private readonly IList<MasterFileEntry> _entries = [];
        private readonly TimeSpan _ttl = DefaultTtl;

        public MasterFile(TimeSpan ttl)
        {
            _ttl = ttl;
        }

        public MasterFile()
        {
        }

        public void Add(IResourceRecord entry)
        {
            _entries.Add(new MasterFileEntry
            {
                ResourceRecord = entry,
                ExpectedRemoteAddress = null,
            });
        }
        
        public void Add(IResourceRecord entry, IPAddress? expectedRemoteAddress)
        {
            _entries.Add(new MasterFileEntry
            {
                ResourceRecord = entry,
                ExpectedRemoteAddress = expectedRemoteAddress?.MapToIPv6(),
            });
        }

        public void AddIpAddressResourceRecord(string domain, string ip)
        {
            AddIpAddressResourceRecord(new Domain(domain), IPAddress.Parse(ip));
        }
        
        public void AddIpAddressResourceRecord(string domain, string ip, string? expectedRemoteIp)
        {
            AddIpAddressResourceRecord(
                new Domain(domain),
                IPAddress.Parse(ip),
                IPAddress.TryParse(expectedRemoteIp, out var parsedRemoteIp) ? parsedRemoteIp : null);
        }

        public void AddIpAddressResourceRecord(Domain domain, IPAddress ip, IPAddress? expectedRemoteIp)
        {
            Add(new IpAddressResourceRecord(domain, ip, _ttl), expectedRemoteIp);
        }
        
        public void AddIpAddressResourceRecord(Domain domain, IPAddress ip)
        {
            Add(new IpAddressResourceRecord(domain, ip, _ttl));
        }

        public void AddNameServerResourceRecord(string domain, string nsDomain)
        {
            AddNameServerResourceRecord(new Domain(domain), new Domain(nsDomain));
        }

        public void AddNameServerResourceRecord(Domain domain, Domain nsDomain)
        {
            Add(new NameServerResourceRecord(domain, nsDomain, _ttl));
        }

        public void AddCanonicalNameResourceRecord(string domain, string cname)
        {
            AddCanonicalNameResourceRecord(new Domain(domain), new Domain(cname));
        }

        public void AddCanonicalNameResourceRecord(Domain domain, Domain cname)
        {
            Add(new CanonicalNameResourceRecord(domain, cname, _ttl));
        }

        public void AddPointerResourceRecord(string ip, string pointer)
        {
            AddPointerResourceRecord(IPAddress.Parse(ip), new Domain(pointer));
        }

        public void AddPointerResourceRecord(IPAddress ip, Domain pointer)
        {
            Add(new PointerResourceRecord(ip, pointer, _ttl));
        }

        public void AddMailExchangeResourceRecord(string domain, int preference, string exchange)
        {
            AddMailExchangeResourceRecord(new Domain(domain), preference, new Domain(exchange));
        }

        public void AddMailExchangeResourceRecord(Domain domain, int preference, Domain exchange)
        {
            Add(new MailExchangeResourceRecord(domain, preference, exchange));
        }

        public void AddTextResourceRecord(string domain, string attributeName, string attributeValue)
        {
            Add(new TextResourceRecord(new Domain(domain), attributeName, attributeValue, _ttl));
        }

        public void AddServiceResourceRecord(Domain domain, ushort priority, ushort weight, ushort port, Domain target)
        {
            Add(new ServiceResourceRecord(domain, priority, weight, port, target, _ttl));
        }

        public void AddServiceResourceRecord(string domain, ushort priority, ushort weight, ushort port, string target)
        {
            AddServiceResourceRecord(new Domain(domain), priority, weight, port, new Domain(target));
        }

        public Task<IResponse> Resolve(IRequest request, IPEndPoint remoteEndPoint, CancellationToken cancellationToken = default(CancellationToken))
        {
            IResponse response = Response.FromRequest(request);

            foreach (Question question in request.Questions)
            {
                var answers = Get(question, remoteEndPoint.Address).ToArray();

                if (answers.Length > 0)
                {
                    Merge(response.AnswerRecords, answers);
                }
                else
                {
                    response.ResponseCode = ResponseCode.NameError;
                }
            }

            return Task.FromResult(response);
        }

        private static void Merge<T>(IList<T> l1, IList<T> l2)
        {
            foreach (T obj in l2)
            {
                l1.Add(obj);
            }
        }

        private IEnumerable<IResourceRecord> Get(Domain domain, RecordType type, IPAddress remoteAddress)
        {
            List<(MasterFileEntry Entry, bool MatchesByIp)>? foundEntries = null;
            var foundMatchByIp = false;
            foreach (var entry in _entries)
            {
                var resourceRecord = entry.ResourceRecord;
                if ((resourceRecord.Type == type || type == RecordType.ANY)
                    && domain.Equals(resourceRecord.Name))
                {
                    foundEntries ??= new List<(MasterFileEntry Entry, bool MatchesByIp)>();
                    var ipEquals = entry.ExpectedRemoteAddress?.Equals(remoteAddress.MapToIPv6()) ?? false;
                    foundEntries.Add((entry, ipEquals));

                    foundMatchByIp |= ipEquals;
                }
            }

            IEnumerable<(MasterFileEntry Entry, bool MatchesByIp)>? foundEntriesEnumerable = foundEntries;

            if (foundEntriesEnumerable is null)
            {
                return [];
            }
            
            if (foundMatchByIp)
            {
                return foundEntriesEnumerable
                    .Where(entry => entry.MatchesByIp)
                    .Select(entry => entry.Entry.ResourceRecord);
            }
            
            return foundEntriesEnumerable
                .Where(entry => entry is { MatchesByIp: false, Entry.ExpectedRemoteAddress: null })
                .Select(entry => entry.Entry.ResourceRecord);
        }

        private IEnumerable<IResourceRecord> Get(Question question, IPAddress remoteAddress)
        {
            return Get(question.Name, question.Type, remoteAddress);
        }

        private record MasterFileEntry
        {
            public required IResourceRecord ResourceRecord { get; init; }
            public IPAddress? ExpectedRemoteAddress { get; init; }
        }
    }
}
