#nullable enable
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
using Charon.Dns.Lib.Protocol;
using Charon.Dns.Lib.Tracing;

namespace Charon.Dns.Lib.Client.RequestResolver
{
    public class TcpRequestResolver : IRequestResolver
    {
        private readonly IPEndPoint _dns;

        public TcpRequestResolver(IPEndPoint dns)
        {
            _dns = dns;
        }

        public async Task<IResponse> Resolve(IRequest request, RequestTrace trace, CancellationToken cancellationToken = default(CancellationToken))
        {
            using (TcpClient tcp = new TcpClient(_dns.AddressFamily))
            {
                await tcp.ConnectAsync(_dns.Address, _dns.Port).ConfigureAwait(false);

                Stream stream = tcp.GetStream();
                var requestSize = request.Size;
                var requestBuffer = ArrayPool<byte>.Shared.Rent(requestSize);

                try
                {
                    request.WriteTo(requestBuffer.AsSpan(0, requestSize));

                    var lengthPrefix = new byte[2];
                    BinaryPrimitives.WriteUInt16BigEndian(lengthPrefix, (ushort)requestSize);

                    await stream.WriteAsync(lengthPrefix, cancellationToken).ConfigureAwait(false);
                    await stream.WriteAsync(requestBuffer.AsMemory(0, requestSize), cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(requestBuffer);
                }

                byte[] buffer = new byte[2];
                await Read(stream, buffer, cancellationToken).ConfigureAwait(false);

                if (BitConverter.IsLittleEndian)
                {
                    Array.Reverse(buffer);
                }

                buffer = new byte[BitConverter.ToUInt16(buffer, 0)];
                await Read(stream, buffer, cancellationToken).ConfigureAwait(false);

                IResponse response = Response.FromArray(buffer);
                return new ClientResponse(request, response, buffer);
            }
        }

        private static async Task Read(Stream stream, byte[] buffer, CancellationToken cancellationToken)
        {
            int length = buffer.Length;
            int offset = 0;
            int size = 0;

            while (length > 0 && (size = await stream.ReadAsync(buffer, offset, length, cancellationToken).ConfigureAwait(false)) > 0)
            {
                offset += size;
                length -= size;
            }

            if (length > 0)
            {
                throw new IOException("Unexpected end of stream");
            }
        }
    }
}
