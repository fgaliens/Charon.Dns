using System.Collections.Generic;
using Charon.Dns.Lib.Protocol.ResourceRecords;

namespace Charon.Dns.Lib.Protocol
{
    public interface IRequest : IMessage
    {
        int Id { get; set; }
        IList<IResourceRecord> AdditionalRecords { get; }
        OperationCode OperationCode { get; set; }
        bool RecursionDesired { get; set; }
    }
}
