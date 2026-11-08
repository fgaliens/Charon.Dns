using System.Collections.Generic;

namespace Charon.Dns.Lib.Protocol.EqualityComparers;

public class HeaderComparer(bool compareId) : IEqualityComparer<Header>
{
    public static HeaderComparer InstanceWithIdComparision { get; } = new(true);
    public static HeaderComparer InstanceWithoutIdComparision { get; } = new(false);

    public bool Equals(Header x, Header y)
    {
        return x.AsVector(compareId) == y.AsVector(compareId);
    }

    public int GetHashCode(Header obj)
    {
        return obj.AsVector().GetHashCode();
    }
}
