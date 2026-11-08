using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using Charon.Dns.Lib.Protocol.ResourceRecords;
using Charon.Dns.Lib.Server;

namespace Charon.Dns.Lib.Protocol.Utils;

/// <summary>
/// EDNS(0) Padding option (RFC 7830) hides the size of encrypted messages.
/// </summary>
internal static class EdnsPadding
{
    private const ushort PaddingOptionCode = 12;
    private const int OptionHeaderSize = 4;
    // RFC 8467, 4.1: recommended block sizes
    private const int RequestBlockSize = 128;
    private const int ResponseBlockSize = 468;
    private const int MaxMessageSize = ushort.MaxValue;

    public static bool IsRequested(IReadOnlyRequest? request)
    {
        if (request is null)
        {
            return false;
        }

        foreach (var record in request.AdditionalRecords)
        {
            if (record.Type == RecordType.OPT)
            {
                return HasOption(record.Data, PaddingOptionCode);
            }
        }

        return false;
    }

    /// <summary>
    /// RFC 7830, 4: a response to a padded query must be padded.
    /// Returns the response as is, if padding would exceed the maximum message size.
    /// </summary>
    public static IResponse PadResponse(IResponse response)
    {
        var padded = new Response(response);
        var additionalRecords = padded.AdditionalRecords;
        var optIndex = FindOpt(additionalRecords);
        if (optIndex < 0)
        {
            // RFC 6891, 7: a response to an EDNS request carries an OPT record
            var newOpt = new ResourceRecord(DnsServer.RootName, [], RecordType.OPT, DnsServer.AdvertisedUdpPayloadSize);
            additionalRecords.Add(newOpt);
            optIndex = additionalRecords.Count - 1;
        }

        var isPadded = TryPad(additionalRecords, optIndex, ResponseBlockSize, () => padded.Size);
        return isPadded ? padded : response;
    }

    /// <summary>
    /// RFC 8467, 4.1: queries sent over an encrypted transport are padded to a multiple of 128 bytes.
    /// A query without EDNS is returned as is: an OPT record added to it would end up in the response
    /// to a client that doesn't support EDNS (RFC 6891, 7).
    /// </summary>
    public static IRequest PadRequest(IRequest request)
    {
        var optIndex = FindOpt(request.AdditionalRecords);
        if (optIndex < 0)
        {
            return request;
        }

        var padded = new Request(request);
        var isPadded = TryPad(padded.AdditionalRecords, optIndex, RequestBlockSize, () => padded.Size);
        return isPadded ? padded : request;
    }

    /// <summary>
    /// Replaces the padding option of the OPT record at <paramref name="optIndex"/>,
    /// so the message size becomes a multiple of <paramref name="blockSize"/>.
    /// </summary>
    private static bool TryPad(IList<IResourceRecord> additionalRecords, int optIndex, int blockSize, Func<int> getSize)
    {
        var opt = additionalRecords[optIndex];
        var options = RemoveOption(opt.Data, PaddingOptionCode);
        if (options is null)
        {
            // Malformed options are passed as is
            return false;
        }

        // Size of the message with an empty padding option
        additionalRecords[optIndex] = CreateOpt(opt, options, paddingLength: 0);
        var unpaddedSize = getSize();
        var paddingLength = (blockSize - unpaddedSize % blockSize) % blockSize;
        if (unpaddedSize + paddingLength > MaxMessageSize)
        {
            return false;
        }

        additionalRecords[optIndex] = CreateOpt(opt, options, paddingLength);
        return true;
    }

    private static int FindOpt(IList<IResourceRecord> records)
    {
        for (var i = 0; i < records.Count; i++)
        {
            if (records[i].Type == RecordType.OPT)
            {
                return i;
            }
        }

        return -1;
    }

    private static ResourceRecord CreateOpt(IResourceRecord opt, byte[] options, int paddingLength)
    {
        // RFC 7830, 3: padding octets are zeros
        var data = new byte[options.Length + OptionHeaderSize + paddingLength];
        options.CopyTo(data, 0);
        var paddingHeader = data.AsSpan(options.Length);
        BinaryPrimitives.WriteUInt16BigEndian(paddingHeader, PaddingOptionCode);
        BinaryPrimitives.WriteUInt16BigEndian(paddingHeader[2..], (ushort)paddingLength);

        return new ResourceRecord(opt.Name, data, RecordType.OPT, opt.Class, opt.TimeToLive);
    }

    private static bool HasOption(byte[] data, ushort optionCode)
    {
        var offset = 0;
        while (offset + OptionHeaderSize <= data.Length)
        {
            var code = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset));
            var length = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset + 2));
            if (code == optionCode)
            {
                return true;
            }

            offset += OptionHeaderSize + length;
        }

        return false;
    }

    /// <returns>Options without <paramref name="optionCode"/>, null if the options are malformed.</returns>
    private static byte[]? RemoveOption(byte[] data, ushort optionCode)
    {
        var result = new byte[data.Length];
        var resultLength = 0;
        var offset = 0;

        while (offset + OptionHeaderSize <= data.Length)
        {
            var code = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset));
            var length = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset + 2));
            var optionSize = OptionHeaderSize + length;
            if (offset + optionSize > data.Length)
            {
                return null;
            }

            if (code != optionCode)
            {
                data.AsSpan(offset, optionSize).CopyTo(result.AsSpan(resultLength));
                resultLength += optionSize;
            }

            offset += optionSize;
        }

        if (offset != data.Length)
        {
            return null;
        }

        Array.Resize(ref result, resultLength);
        return result;
    }
}
