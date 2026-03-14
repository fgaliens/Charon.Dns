using System;
using System.Collections.Generic;
using Charon.Dns.Lib.Protocol.ResourceRecords;

namespace Charon.Dns.Lib.Protocol.EqualityComparers;

public class ResourceRecordComparer : IEqualityComparer<IResourceRecord>
{
    public static ResourceRecordComparer Instance { get; } = new ResourceRecordComparer();

    public bool Equals(IResourceRecord x, IResourceRecord y)
    {
        if (ReferenceEquals(x, y)) 
            return true;
        if (x is null) 
            return false;
        if (y is null) 
            return false;
        if (x.GetType() != y.GetType()) 
            return false;
        return x.TimeToLive.Equals(y.TimeToLive)
            && x.DataLength == y.DataLength
            && x.Type == y.Type
            && x.Class == y.Class
            && x.Size == y.Size
            && x.Data.SequenceEqual(y.Data)
            && x.Name.CompareTo(y.Name) == 0;
    }

    public int GetHashCode(IResourceRecord obj)
    {
        var hashCode = new HashCode();
        hashCode.Add(obj.TimeToLive);
        hashCode.Add(obj.DataLength);
        hashCode.Add(obj.Name);
        hashCode.Add((int)obj.Type);
        hashCode.Add((int)obj.Class);
        hashCode.Add(obj.Size);
        hashCode.AddBytes(obj.Data);

        return hashCode.ToHashCode();
    }
}
