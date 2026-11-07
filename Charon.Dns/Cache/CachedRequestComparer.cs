using Charon.Dns.Lib.Protocol;
using Charon.Dns.Lib.Protocol.EqualityComparers;

namespace Charon.Dns.Cache;

/// <summary>
/// Matches requests that can share a cached response.
/// EDNS options (cookies, padding, client subnet) are unique per request,
/// so only the presence of EDNS and its DO flag are compared.
/// </summary>
public class CachedRequestComparer : IEqualityComparer<IReadOnlyRequest>
{
    public static CachedRequestComparer Instance { get; } = new();

    public bool Equals(IReadOnlyRequest? x, IReadOnlyRequest? y)
    {
        if (ReferenceEquals(x, y))
        {
            return true;
        }

        if (x is null || y is null)
        {
            return false;
        }

        if (x.OperationCode != y.OperationCode
            || x.RecursionDesired != y.RecursionDesired)
        {
            return false;
        }

        if (!x.Questions.SequenceEqual(y.Questions, QuestionComparer.Instance))
        {
            return false;
        }

        var xEdnsState = GetEdnsState(x);
        var yEdnsState = GetEdnsState(y);
        if (xEdnsState != yEdnsState)
        {
            return false;
        }

        var yNonEdnsRecords = y.AdditionalRecords.Where(record => record.Type != RecordType.OPT);
        return x.AdditionalRecords
            .Where(record => record.Type != RecordType.OPT)
            .SequenceEqual(yNonEdnsRecords, ResourceRecordComparer.Instance);
    }

    public int GetHashCode(IReadOnlyRequest obj)
    {
        var hashCode = new HashCode();
        hashCode.Add(obj.OperationCode);
        hashCode.Add(obj.RecursionDesired);

        foreach (var question in obj.Questions)
        {
            hashCode.Add(question, QuestionComparer.Instance);
        }

        var ednsState = GetEdnsState(obj);
        hashCode.Add(ednsState);

        return hashCode.ToHashCode();
    }

    private static EdnsState GetEdnsState(IReadOnlyRequest request)
    {
        foreach (var record in request.AdditionalRecords)
        {
            if (record.Type == RecordType.OPT)
            {
                return record.IsDnssecOk() ? EdnsState.DnssecOk : EdnsState.Enabled;
            }
        }

        return EdnsState.Disabled;
    }

    private enum EdnsState
    {
        Disabled,
        Enabled,
        DnssecOk,
    }
}
