using System;
using System.Threading;
using System.Threading.Tasks;

namespace Charon.Dns.Lib.Protocol.Utils;

public static class TaskExtensions
{
    extension<T>(Task<T> task)
    {
        public async Task<T> WithCancellation(CancellationToken token)
        {
            var cancellationSource = new TaskCompletionSource();
            await using var registration = token.Register(() => cancellationSource.TrySetResult());

            var completedTask = await Task.WhenAny(task, cancellationSource.Task).ConfigureAwait(false);
            if (completedTask != task)
            {
                throw new OperationCanceledException(token);
            }

            return await task.ConfigureAwait(false);
        }

        public async Task<T> WithCancellationTimeout(TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            using var timeoutSource = new CancellationTokenSource(timeout);
            using var linkSource = CancellationTokenSource.CreateLinkedTokenSource(timeoutSource.Token, cancellationToken);

            return await task.WithCancellation(linkSource.Token).ConfigureAwait(false);
        }
    }
}
