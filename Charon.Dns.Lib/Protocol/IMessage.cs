using System.Collections.Generic;

namespace Charon.Dns.Lib.Protocol
{
    public interface IMessage
    {
        IList<Question> Questions { get; }

        int Size { get; }
        byte[] ToArray();
    }
}
