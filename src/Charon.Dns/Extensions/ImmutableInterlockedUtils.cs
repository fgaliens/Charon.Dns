using System.Collections.Immutable;

namespace Charon.Dns.Extensions;

public static class ImmutableInterlockedUtils
{
    public static void Add<T>(ref ImmutableSortedSet<T> location, T value)
    {
        ImmutableSortedSet<T> priorCollection = Volatile.Read(ref location);
        bool successful;
        do
        {
            ImmutableSortedSet<T> updatedCollection = priorCollection.Add(value);
            ImmutableSortedSet<T> interlockedResult = Interlocked.CompareExchange(ref location, updatedCollection, priorCollection);
            successful = ReferenceEquals(priorCollection, interlockedResult);
            priorCollection = interlockedResult;
        } while (!successful);
    }
    
    public static void Remove<T>(ref ImmutableSortedSet<T> location, T value)
    {
        ImmutableSortedSet<T> priorCollection = Volatile.Read(ref location);
        bool successful;
        do
        {
            ImmutableSortedSet<T> updatedCollection = priorCollection.Remove(value);
            ImmutableSortedSet<T> interlockedResult = Interlocked.CompareExchange(ref location, updatedCollection, priorCollection);
            successful = ReferenceEquals(priorCollection, interlockedResult);
            priorCollection = interlockedResult;
        } while (!successful);
    }
}
