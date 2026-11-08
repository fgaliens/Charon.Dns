using System;
using Charon.Dns.Lib.Protocol.Utils;

namespace Charon.Dns.Lib.Protocol.ResourceRecords;

public abstract class BaseResourceRecord(IResourceRecord record) : IResourceRecord
{
    public Domain Name => record.Name;

    public RecordType Type => record.Type;

    public RecordClass Class => record.Class;

    public TimeSpan TimeToLive => record.TimeToLive;

    public int DataLength => record.DataLength;

    public byte[] Data => record.Data;

    public int Size => record.Size;

    public byte[] ToArray()
    {
        return record.ToArray();
    }

    public void WriteTo(Span<byte> destination)
    {
        record.WriteTo(destination);
    }

    internal ObjectStringifier Stringify()
    {
        return ObjectStringifier.New(this)
            .Add(nameof(Name), nameof(Type), nameof(Class), nameof(TimeToLive), nameof(DataLength));
    }
}
