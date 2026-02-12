namespace Charon.Dns.Extensions;

public static class EnumerableExtensions
{
    public static IEnumerable<(TKey Key, TValue Value)> TurnOut<TKey, TValue>(this IEnumerable<TValue> values, Func<TValue, IEnumerable<TKey>> keysSelector)
    {
        foreach (var value in values)
        {
            var keyValues = keysSelector(value);
            foreach (var keyValue in keyValues)
            {
                yield return (keyValue, value);
            }
        }
    }
}
