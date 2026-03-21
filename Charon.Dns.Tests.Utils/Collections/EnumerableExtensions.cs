namespace Charon.Dns.Tests.Utils.Collections;

public static class EnumerableExtensions
{
    extension<T>(IEnumerable<T> sequence)
    {
        public IEnumerable<(T, TOther)> CombineWith<TOther>(IEnumerable<TOther> otherSequence)
        {
            var otherSequenceArray = otherSequence.ToArray();
            foreach (var item1 in sequence)
            {
                foreach (var item2 in otherSequenceArray)
                {
                    yield return (item1, item2);
                }
            }
        }
    }
}
