using System;
using System.Threading;
using System.Threading.Tasks;

namespace AdaskoTheBeAsT.Interop.Threading;

/// <summary>
/// http://stackoverflow.com/questions/4238345/asynchronously-wait-for-taskt-to-complete-with-timeout.
/// </summary>
public static class TaskExtension
{
    public static async Task<TResult> TimeoutAfterAsync<TResult>(
        this Task<TResult> task,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        // Link the caller’s token with our timeout token
        using var timeoutCts = new CancellationTokenSource();
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        // Start a delay that will fire timeoutCts after the interval
        var delayTask = Task.Delay(timeout, timeoutCts.Token);

        // Wait for either the original task OR the delay
#pragma warning disable VSTHRD003
        var finished = await Task.WhenAny(task, delayTask).ConfigureAwait(false);
#pragma warning restore VSTHRD003

        if (finished == delayTask)
        {
            // signal cancellation to the linked token (so if `task` honors it, it can stop)
#if NETSTANDARD2_0
            timeoutCts.Cancel();
#endif

#if NET8_0_OR_GREATER
            await timeoutCts.CancelAsync().ConfigureAwait(false);
#endif
            throw new TimeoutException($"The operation has timed out after {timeout}.");
        }

        // Cancel the delay (to clean up) and propagate any exceptions
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
}
