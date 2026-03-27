using System;
using System.Threading;
using System.Threading.Tasks;

namespace Charon.Dns.Lib.Concurrency;

public class EmptyConcurrencyLimiter : IConcurrencyLimiter
{
    private static Task<IDisposable> LimiterFakeScope { get; } = Task.FromResult(new FakeScope() as IDisposable);
    
    public static EmptyConcurrencyLimiter Instance { get; } = new();
    
    public Task<IDisposable> WaitAsync(CancellationToken token = default)
    {
        return LimiterFakeScope;
    }
    
    private class FakeScope : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
