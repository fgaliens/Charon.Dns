using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Charon.Dns.Lib.Client.RequestResolver;
using Charon.Dns.Lib.Protocol;
using Charon.Dns.Lib.Protocol.ResourceRecords;
using Serilog.Core;

namespace Charon.Dns.Lib.Client;

public class DnsClient(IRequestResolver resolver)
{
    private const int DefaultPort = 53;

    public DnsClient(IPEndPoint dns)
        : this(new UdpRequestResolver(dns, null, Logger.None))
    {
    }

    public DnsClient(IPAddress ip, int port = DefaultPort)
        : this(new IPEndPoint(ip, port))
    {
    }

    public DnsClient(string ip, int port = DefaultPort)
        : this(IPAddress.Parse(ip), port)
    {
    }

    public ClientRequest FromArray(byte[] message)
    {
        var request = Request.FromArray(message);
        return new ClientRequest(resolver, request);
    }

    public ClientRequest Create(IRequest? request = null)
    {
        return new ClientRequest(resolver, request);
    }

    public async Task<IList<IPAddress>> Lookup(string domain, RecordType type = RecordType.A, CancellationToken cancellationToken = default)
    {
        if (type is not (RecordType.A or RecordType.AAAA))
        {
            throw new ArgumentException($"Invalid record type {type}");
        }

        var response = await Resolve(domain, type, cancellationToken).ConfigureAwait(false);
        var ips = response.AnswerRecords
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

    public Task<string> Reverse(string ip, CancellationToken cancellationToken = default)
    {
        var address = IPAddress.Parse(ip);
        return Reverse(address, cancellationToken);
    }

    public async Task<string> Reverse(IPAddress ip, CancellationToken cancellationToken = default)
    {
        var pointerName = Domain.PointerName(ip);
        var response = await Resolve(pointerName, RecordType.PTR, cancellationToken).ConfigureAwait(false);
        var pointer = response.AnswerRecords.OfType<PointerResourceRecord>().FirstOrDefault();

        if (pointer is null)
        {
            throw new ResponseException(response, "No matching records");
        }

        return pointer.PointerDomainName.ToString();
    }

    public Task<IResponse> Resolve(string domain, RecordType type, CancellationToken cancellationToken = default)
    {
        var name = new Domain(domain);
        return Resolve(name, type, cancellationToken);
    }

    public async Task<IResponse> Resolve(Domain domain, RecordType type, CancellationToken cancellationToken = default)
    {
        var request = Create();
        var question = new Question(domain, type);

        request.Questions.Add(question);
        request.OperationCode = OperationCode.Query;
        request.RecursionDesired = true;

        var response = await request.Resolve(cancellationToken).ConfigureAwait(false);
        if (request.Id != response.Id)
        {
            throw new InvalidOperationException("Request and response identifiers are different");
        }

        return response;
    }
}
