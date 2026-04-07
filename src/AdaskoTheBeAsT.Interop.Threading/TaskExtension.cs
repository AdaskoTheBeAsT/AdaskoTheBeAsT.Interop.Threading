using System;
using System.Threading;
using System.Threading.Tasks;

namespace AdaskoTheBeAsT.Interop.Threading;

/// <summary>
/// Provides timeout helpers for tasks.
/// </summary>
public static class TaskExtension
{
    /// <summary>
    /// Waits for a task to complete, enforcing a timeout while still honoring caller cancellation.
    /// </summary>
    /// <typeparam name="TResult">The result type of the task.</typeparam>
    /// <param name="task">The task to await.</param>
    /// <param name="timeout">The maximum amount of time to wait before throwing a <see cref="TimeoutException"/>.</param>
    /// <param name="cancellationToken">A token that cancels the wait before the timeout expires.</param>
    /// <returns>A task that produces the original result when the operation completes in time.</returns>
    /// <exception cref="TimeoutException">Thrown when the timeout expires before the task completes.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is canceled before the task completes.</exception>
    public static async Task<TResult> TimeoutAfterAsync<TResult>(
        this Task<TResult> task,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = new CancellationTokenSource();
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        var delayTask = Task.Delay(timeout, linkedCts.Token);

#pragma warning disable VSTHRD003
        var finished = await Task.WhenAny(task, delayTask).ConfigureAwait(false);
#pragma warning restore VSTHRD003

        if (finished == task)
        {
#if NETSTANDARD2_0
            timeoutCts.Cancel();
#endif

#if NET8_0_OR_GREATER
            await timeoutCts.CancelAsync().ConfigureAwait(false);
#endif
#pragma warning disable VSTHRD003
            return await task.ConfigureAwait(false);
#pragma warning restore VSTHRD003
        }

        cancellationToken.ThrowIfCancellationRequested();
        throw new TimeoutException($"The operation has timed out after {timeout}.");
    }
}
