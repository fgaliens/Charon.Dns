using System.Collections.Generic;
using Charon.Dns.Lib.Protocol.ResourceRecords;

namespace Charon.Dns.Lib.Protocol
{
    public interface IResponse : IMessage
    {
        int Id { get; set; }
        IList<IResourceRecord> AnswerRecords { get; }
        IList<IResourceRecord> AuthorityRecords { get; }
        IList<IResourceRecord> AdditionalRecords { get; }
        bool RecursionAvailable { get; set; }
        bool AuthenticData { get; set; }
        bool CheckingDisabled { get; set; }
        bool AuthorativeServer { get; set; }
        bool Truncated { get; set; }
        OperationCode OperationCode { get; set; }
        ResponseCode ResponseCode { get; set; }
    }
}
