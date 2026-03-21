using System;
using System.Collections.Generic;

namespace Charon.Dns.Lib.Protocol.EqualityComparers;

public class QuestionComparer : IEqualityComparer<Question>
{
    public static QuestionComparer Instance { get; } = new QuestionComparer();
    
    public bool Equals(Question x, Question y)
    {
        if (ReferenceEquals(x, y))
        {
            return true;
        }

        if (ReferenceEquals(x, null) || ReferenceEquals(y, null))
        {
            return false;
        }

        return x.Type == y.Type
            && x.Class == y.Class
            && x.Name.Equals(y.Name);
    }

    public int GetHashCode(Question obj)
    {
        return HashCode.Combine(obj.Type, obj.Class, obj.Name.GetHashCode());
    }
}
