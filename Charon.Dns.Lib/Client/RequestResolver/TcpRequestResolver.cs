using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Charon.Dns.Lib.Protocol;
using Charon.Dns.Lib.Tracing;

namespace Charon.Dns.Lib.Client.RequestResolver;

public class TcpRequestResolver(IPEndPoint dns) : IRequestResolver
{
    private const int LengthPrefixSize = 2;

    public async Task<IResponse> Resolve(IRequest request, RequestTrace trace, CancellationToken cancellationToken = default)
    {
        using var tcp = new TcpClient(dns.AddressFamily);
        await tcp.ConnectAsync(dns.Address, dns.Port, cancellationToken).ConfigureAwait(false);

        var stream = tcp.GetStream();
        var requestSize = request.Size;
        var requestBuffer = ArrayPool<byte>.Shared.Rent(LengthPrefixSize + requestSize);

        try
        {
            // RFC 1035, 4.2.2: a TCP message is prefixed by its length
            BinaryPrimitives.WriteUInt16BigEndian(requestBuffer, (ushort)requestSize);
            request.WriteTo(requestBuffer.AsSpan(LengthPrefixSize, requestSize));

            var requestMemory = requestBuffer.AsMemory(0, LengthPrefixSize + requestSize);
            await stream.WriteAsync(requestMemory, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(requestBuffer);
        }

        var lengthPrefix = new byte[LengthPrefixSize];
        await stream.ReadExactlyAsync(lengthPrefix, cancellationToken).ConfigureAwait(false);

        var responseSize = BinaryPrimitives.ReadUInt16BigEndian(lengthPrefix);
        var buffer = new byte[responseSize];
        await stream.ReadExactlyAsync(buffer, cancellationToken).ConfigureAwait(false);

        var response = Response.FromArray(buffer);
        return new ClientResponse(request, response, buffer);
    }
}
