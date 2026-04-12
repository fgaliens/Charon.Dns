using System;
using System.Collections.Generic;
using Charon.Dns.Lib.Protocol.ResourceRecords;

namespace Charon.Dns.Lib.Protocol
{
    public interface IRequest : IReadOnlyRequest, IMessage, IEquatable<IRequest>
    {
        new int Id { get; set; }
        new IList<Question> Questions { get; }
        new IList<IResourceRecord> AdditionalRecords { get; }
        new OperationCode OperationCode { get; set; }
        new bool RecursionDesired { get; set; }
    }
}
