using System.Buffers.Binary;
using Charon.Dns.Lib.Protocol;
using Charon.Dns.Lib.Protocol.ResourceRecords;

namespace Charon.Dns.Cache;

/// <summary>
/// Helpers for the EDNS OPT pseudo-record (RFC 6891): its TTL holds extended flags, its data holds options.
/// </summary>
public static class EdnsRecordExtensions
{
    private const uint DnssecOkFlag = 0x8000;
    private const ushort CookieOptionCode = 10;
    private const int OptionHeaderSize = 4;

    extension(IResourceRecord record)
    {
        public bool IsDnssecOk()
        {
            var flags = (uint)record.TimeToLive.TotalSeconds;
            return (flags & DnssecOkFlag) != 0;
        }

        /// <summary>
        /// A cookie (RFC 7873) is bound to the client that sent it,
        /// a client receiving someone else's cookie drops the response.
        /// </summary>
        public IResourceRecord WithoutCookie()
        {
            if (record.Type != RecordType.OPT)
            {
                return record;
            }

            var data = record.Data;
            var strippedData = new byte[data.Length];
            var strippedLength = 0;
            var offset = 0;

            while (offset + OptionHeaderSize <= data.Length)
            {
                var code = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset));
                var length = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset + 2));
                var optionSize = OptionHeaderSize + length;
                if (offset + optionSize > data.Length)
                {
                    // Malformed options are passed as is
                    return record;
                }

                if (code != CookieOptionCode)
                {
                    data.AsSpan(offset, optionSize).CopyTo(strippedData.AsSpan(strippedLength));
                    strippedLength += optionSize;
                }

                offset += optionSize;
            }

            if (offset != data.Length || strippedLength == data.Length)
            {
                return record;
            }

            Array.Resize(ref strippedData, strippedLength);
            return new ResourceRecord(record.Name, strippedData, record.Type, record.Class, record.TimeToLive);
        }
    }
}
