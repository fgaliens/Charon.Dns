using System.Collections.Generic;
using Charon.Dns.Lib.Protocol.ResourceRecords;

namespace Charon.Dns.Lib.Protocol
{
    public interface IResponse : IReadOnlyResponse, IMessage
    {
        new int Id { get; set; }
        new IList<Question> Questions { get; }
        new IList<IResourceRecord> AnswerRecords { get; }
        new IList<IResourceRecord> AuthorityRecords { get; }
        new IList<IResourceRecord> AdditionalRecords { get; }
        new bool RecursionAvailable { get; set; }
        new bool AuthenticData { get; set; }
        new bool CheckingDisabled { get; set; }
        new bool AuthorativeServer { get; set; }
        new bool Truncated { get; set; }
        new OperationCode OperationCode { get; set; }
        new ResponseCode ResponseCode { get; set; }
    }
}
