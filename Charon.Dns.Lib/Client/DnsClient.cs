using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Threading;
using Charon.Dns.Lib.Client.RequestResolver;
using Charon.Dns.Lib.Protocol;
using Charon.Dns.Lib.Protocol.ResourceRecords;
using Serilog.Core;

namespace Charon.Dns.Lib.Client
{
    public class DnsClient
    {
        private const int DefaultPort = 53;

        private readonly IRequestResolver _resolver;

        public DnsClient(IPEndPoint dns) :
            this(new UdpRequestResolver(dns, null, Logger.None))
        { }

        public DnsClient(IPAddress ip, int port = DefaultPort) :
            this(new IPEndPoint(ip, port))
        { }

        public DnsClient(string ip, int port = DefaultPort) :
            this(IPAddress.Parse(ip), port)
        { }

        public DnsClient(IRequestResolver resolver)
        {
            _resolver = resolver;
        }

        public ClientRequest FromArray(byte[] message)
        {
            Request request = Request.FromArray(message);
            return new ClientRequest(_resolver, request);
        }

        public ClientRequest Create(IRequest request = null)
        {
            return new ClientRequest(_resolver, request);
        }

        public async Task<IList<IPAddress>> Lookup(string domain, RecordType type = RecordType.A, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (type != RecordType.A && type != RecordType.AAAA)
            {
                throw new ArgumentException("Invalid record type " + type);
            }

            IResponse response = await Resolve(domain, type, cancellationToken).ConfigureAwait(false);
            IList<IPAddress> ips = response.AnswerRecords
                .Where(r => r.Type == type)
                .Cast<IpAddressResourceRecord>()
                .Select(r => r.IpAddress)
                .ToList();

            if (ips.Count == 0)
            {
                throw new ResponseException(response, "No matching records");
            }

            return ips;
        }

        public Task<string> Reverse(string ip, CancellationToken cancellationToken = default(CancellationToken))
        {
            return Reverse(IPAddress.Parse(ip), cancellationToken);
        }

        public async Task<string> Reverse(IPAddress ip, CancellationToken cancellationToken = default(CancellationToken))
        {
            IResponse response = await Resolve(Domain.PointerName(ip), RecordType.PTR, cancellationToken).ConfigureAwait(false);
            IResourceRecord ptr = response.AnswerRecords.FirstOrDefault(r => r.Type == RecordType.PTR);

            if (ptr == null)
            {
                throw new ResponseException(response, "No matching records");
            }

            return ((PointerResourceRecord)ptr).PointerDomainName.ToString();
        }

        public Task<IResponse> Resolve(string domain, RecordType type, CancellationToken cancellationToken = default(CancellationToken))
        {
            return Resolve(new Domain(domain), type, cancellationToken);
        }

        public async Task<IResponse> Resolve(Domain domain, RecordType type, CancellationToken cancellationToken = default(CancellationToken))
        {
            ClientRequest request = Create();
            Question question = new Question(domain, type);

            request.Questions.Add(question);
            request.OperationCode = OperationCode.Query;
            request.RecursionDesired = true;

            var response = await request.Resolve(cancellationToken);
            if (request.Id != response.Id)
            {
                throw new InvalidOperationException("Request and response identifiers are different");
            }
            return response;
        }
    }
}
