using System;
using System.Threading.Tasks;

namespace Charon.Dns.Lib.AsyncEvents;

public static class AsyncObserver
{
    public static IAsyncObserver<T> Create<T>(Func<T, Task> onEventHandler)
    {
        return new DefaultAsyncObserver<T>(onEventHandler, null);
    }
    
    private class DefaultAsyncObserver<T>(
        Func<T, Task>? onEventHandler, 
        Func<Task>? onCompletedHandler) 
        : IAsyncObserver<T>
    {
        public async Task OnEvent(T eventArgs)
        {
            await (onEventHandler?.Invoke(eventArgs) ?? Task.CompletedTask);
        }

        public async Task OnCompleted()
        {
            await (onCompletedHandler?.Invoke() ?? Task.CompletedTask);
        }
    }   
}
