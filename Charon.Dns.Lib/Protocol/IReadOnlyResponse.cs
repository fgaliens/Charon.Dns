using System.Collections.Generic;
using Charon.Dns.Lib.Protocol.ResourceRecords;

namespace Charon.Dns.Lib.Protocol
{
    public interface IReadOnlyResponse
    {
        int Id { get; }
        IReadOnlyList<Question> Questions { get; }
        IReadOnlyList<IResourceRecord> AnswerRecords { get; }
        IReadOnlyList<IResourceRecord> AuthorityRecords { get; }
        IReadOnlyList<IResourceRecord> AdditionalRecords { get; }
        bool RecursionAvailable { get; }
        bool AuthenticData { get; }
        bool CheckingDisabled { get; }
        bool AuthorativeServer { get; }
        bool Truncated { get; }
        OperationCode OperationCode { get; }
        ResponseCode ResponseCode { get; }
    }
}
