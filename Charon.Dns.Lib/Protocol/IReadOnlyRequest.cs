using System.Collections.Generic;
using Charon.Dns.Lib.Protocol.ResourceRecords;

namespace Charon.Dns.Lib.Protocol
{
    public interface IReadOnlyRequest
    {
        int Id { get; }
        IReadOnlyList<Question> Questions { get; }
        IReadOnlyList<IResourceRecord> AdditionalRecords { get; }
        OperationCode OperationCode { get; }
        bool RecursionDesired { get; }
    }
}
