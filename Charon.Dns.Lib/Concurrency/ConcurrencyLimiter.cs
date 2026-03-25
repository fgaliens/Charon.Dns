using System;
using System.Threading;
using System.Threading.Tasks;

namespace Charon.Dns.Lib.Concurrency;

public class ConcurrencyLimiter : IConcurrencyLimiter
{
    private readonly SemaphoreSlim _limiter;

    public ConcurrencyLimiter(int tasksCount)
    {

        ArgumentOutOfRangeException.ThrowIfLessThan(tasksCount, 1);
        _limiter = new SemaphoreSlim(tasksCount, tasksCount);
    }

    public async Task<IDisposable> WaitAsync(CancellationToken token = default)
    {
        await _limiter.WaitAsync(token);
        return new LimiterScope(this, true);
    }
    
    private class LimiterScope(ConcurrencyLimiter limiter, bool lockTaken) : IDisposable
    {
        public void Dispose()
        {
            if (lockTaken)
            {
                limiter._limiter.Release();
            }
        }
    }
}
