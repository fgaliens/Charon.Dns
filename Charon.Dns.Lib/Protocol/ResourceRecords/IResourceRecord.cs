using System;

namespace Charon.Dns.Lib.Protocol.ResourceRecords
{
    public interface IResourceRecord : IMessageEntry
    {
        TimeSpan TimeToLive { get; }
        int DataLength { get; }
        byte[] Data { get; }
    }
}
