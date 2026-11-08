using System;

namespace Charon.Dns.Lib.AsyncEvents;

public interface IAsyncObservable<out T>
{
    IAsyncDisposable Subscribe(IAsyncObserver<T> observer);
}