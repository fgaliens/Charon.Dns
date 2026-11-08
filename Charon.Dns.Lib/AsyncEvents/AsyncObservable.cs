using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;

namespace Charon.Dns.Lib.AsyncEvents;

public class AsyncObservable<T> : IAsyncObservable<T>, IAsyncEventEmitter<T>
{
    private ImmutableDictionary<IAsyncObserver<T>, bool> _observers = ImmutableDictionary<IAsyncObserver<T>, bool>.Empty;
    
    public IAsyncDisposable Subscribe(IAsyncObserver<T> observer)
    {
        ImmutableInterlocked.TryAdd(ref _observers, observer, true);
        return new Subscription(this, observer);
    }

    public async Task SendEvent(T eventArgs)
    {
        var tasks = _observers.Select(x => x.Key.OnEvent(eventArgs));
        await Task.WhenAll(tasks);
    }
    
    private class Subscription(
        AsyncObservable<T> observable,
        IAsyncObserver<T> observer) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            ImmutableInterlocked.TryRemove(ref observable._observers, observer, out _);
            await observer.OnCompleted();
        }
    }
}
