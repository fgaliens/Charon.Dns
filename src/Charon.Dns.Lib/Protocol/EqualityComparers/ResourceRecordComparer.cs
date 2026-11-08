using System;
using System.Collections.Generic;
using Charon.Dns.Lib.Protocol.ResourceRecords;

namespace Charon.Dns.Lib.Protocol.EqualityComparers;

public class ResourceRecordComparer : IEqualityComparer<IResourceRecord>
{
    public static ResourceRecordComparer Instance { get; } = new();

    public bool Equals(IResourceRecord? x, IResourceRecord? y)
    {
        if (ReferenceEquals(x, y))
        {
            return true;
        }

        if (x is null || y is null || x.GetType() != y.GetType())
        {
            return false;
        }

        return x.DataLength == y.DataLength
            && x.Type == y.Type
            && x.Class == y.Class
            && x.Size == y.Size
            && x.Name.Equals(y.Name)
            && x.Data.AsSpan().SequenceEqual(y.Data);
    }

    public int GetHashCode(IResourceRecord obj)
    {
        var hashCode = new HashCode();
        hashCode.Add(obj.DataLength);
        hashCode.Add(obj.Name);
        hashCode.Add(obj.Type);
        hashCode.Add(obj.Class);
        hashCode.Add(obj.Size);
        hashCode.AddBytes(obj.Data);

        return hashCode.ToHashCode();
    }
}
