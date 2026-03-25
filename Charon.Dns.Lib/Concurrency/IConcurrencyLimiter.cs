using System;
using System.Threading;
using System.Threading.Tasks;

namespace Charon.Dns.Lib.Concurrency;

public interface IConcurrencyLimiter
{
    Task<IDisposable> WaitAsync(CancellationToken token = default);
}
