using System;
using System.Net;

namespace Charon.Dns.Lib.Protocol.ResourceRecords
{
    public class PointerResourceRecord : BaseResourceRecord
    {
        public PointerResourceRecord(IResourceRecord record, byte[] message, int dataOffset)
            : base(Rebuild(record, message, dataOffset, out var pointer))
        {
            PointerDomainName = pointer;
        }

        public PointerResourceRecord(IPAddress ip, Domain pointer, TimeSpan ttl = default(TimeSpan)) :
            base(new ResourceRecord(Domain.PointerName(ip), pointer.ToArray(), RecordType.PTR, RecordClass.IN, ttl))
        {
            PointerDomainName = pointer;
        }

        public Domain PointerDomainName { get; }

        public override string ToString()
        {
            return Stringify().Add("PointerDomainName").ToString();
        }

        private static IResourceRecord Rebuild(IResourceRecord record, byte[] message, int dataOffset, out Domain pointer)
        {
            pointer = Domain.FromArray(message, dataOffset);
            return new ResourceRecord(record.Name, pointer.ToArray(), record.Type, record.Class, record.TimeToLive);
        }
    }
}
