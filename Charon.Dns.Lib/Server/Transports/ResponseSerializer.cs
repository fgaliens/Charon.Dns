using System;
using System.Buffers;
using Charon.Dns.Lib.Protocol;

namespace Charon.Dns.Lib.Server.Transports;

/// <summary>
/// Serializes responses of transports without the UDP size limit: TCP, TLS and HTTPS.
/// </summary>
internal static class ResponseSerializer
{
    public const int MaxMessageSize = ushort.MaxValue;

    private static readonly ArrayPool<byte> ArrayPool = ArrayPool<byte>.Shared;

    /// <summary>
    /// Writes the response after <paramref name="prefixSize"/> bytes reserved for framing.
    /// The buffer is rented and must be returned with <see cref="Return"/>.
    /// </summary>
    public static byte[] Rent(IResponse response, bool pad, int prefixSize, out int size)
    {
        if (pad)
        {
            response = EdnsPadding.Pad(response);
        }

        size = response.Size;
        if (size > MaxMessageSize)
        {
            response = DnsServer.Truncate(response);
            size = response.Size;
        }

        var buffer = ArrayPool.Rent(prefixSize + size);
        try
        {
            response.WriteTo(buffer.AsSpan(prefixSize, size));
        }
        catch
        {
            ArrayPool.Return(buffer);
            throw;
        }

        return buffer;
    }

    public static void Return(byte[] buffer)
    {
        ArrayPool.Return(buffer, clearArray: true);
    }
}
