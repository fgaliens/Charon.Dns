using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Charon.Dns.Lib.Protocol;
using Charon.Dns.Lib.Protocol.ResourceRecords;
using Charon.Dns.Lib.Server;
using Charon.Dns.Lib.Tracing;
using Charon.Dns.Utils.Units;
using Serilog;

namespace Charon.Dns.Lib.Client.RequestResolver;

/// <summary>
/// Plain DNS: UDP, with TCP for responses that don't fit into a datagram.
/// </summary>
/// <remarks>
/// RFC 1035, 4.2.1; RFC 2181, 9; RFC 7766, 5: a truncated response is retried over TCP,
/// the truncated one is never returned.
/// </remarks>
public sealed class UdpAndTcpRequestResolver : IRequestResolver, IDisposable
{
    private readonly UdpRequestResolver _udpResolver;
    private readonly TcpRequestResolver _tcpResolver;

    public UdpAndTcpRequestResolver(IPEndPoint dnsEndpoint, ByteUnit? socketBufferSize, ILogger logger)
    {
        _udpResolver = new UdpRequestResolver(dnsEndpoint, socketBufferSize, logger);
        _tcpResolver = new TcpRequestResolver(dnsEndpoint, logger);
    }

    public async Task<IResponse> Resolve(IRequest request, RequestTrace trace, CancellationToken cancellationToken = default)
    {
        var udpRequest = LimitUdpPayloadSize(request);
        var response = await _udpResolver.Resolve(udpRequest, trace, cancellationToken);
        if (!response.Truncated)
        {
            return response;
        }

        trace.Logger.Debug("Request resolving. Response is truncated, retrying over TCP");
        return await _tcpResolver.Resolve(request, trace, cancellationToken);
    }

    public void Dispose()
    {
        _udpResolver.Dispose();
        _tcpResolver.Dispose();
    }

    /// <summary>
    /// RFC 6891, 6.2.5; DNS Flag Day 2020: a payload size above 1232 bytes leads to fragmented datagrams,
    /// which are often dropped. A larger response is truncated by the server and retried over TCP instead.
    /// </summary>
    private static IRequest LimitUdpPayloadSize(IRequest request)
    {
        var additionalRecords = request.AdditionalRecords;
        for (var i = 0; i < additionalRecords.Count; i++)
        {
            var record = additionalRecords[i];
            if (record.Type != RecordType.OPT)
            {
                continue;
            }

            // The class of the OPT record holds the UDP payload size
            if (record.Class <= DnsServer.AdvertisedUdpPayloadSize)
            {
                return request;
            }

            var limitedRequest = new Request(request);
            var limitedOpt = new ResourceRecord(record.Name, record.Data, RecordType.OPT,
                DnsServer.AdvertisedUdpPayloadSize, record.TimeToLive);
            limitedRequest.AdditionalRecords[i] = limitedOpt;
            return limitedRequest;
        }

        return request;
    }
}
