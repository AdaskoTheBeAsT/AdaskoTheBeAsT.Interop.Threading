using System;
using System.Threading;
using System.Threading.Tasks;

namespace AdaskoTheBeAsT.Interop.Threading;

/// <summary>
/// http://stackoverflow.com/questions/4238345/asynchronously-wait-for-taskt-to-complete-with-timeout.
/// </summary>
public static class TaskExtension
{
    public static async Task<TResult> TimeoutAfterAsync<TResult>(this Task<TResult> task, TimeSpan timeout)
    {
        using (var timeoutCancellationTokenSource = new CancellationTokenSource())
        {
            // disable warning as this is targeted only for web api projects
#pragma warning disable VSTHRD003
            var completedTask = await Task.WhenAny(task, Task.Delay(timeout, timeoutCancellationTokenSource.Token)).ConfigureAwait(false);
#pragma warning restore VSTHRD003
            if (completedTask != task)
            {
                throw new TimeoutException("The operation has timed out.");
            }

#if NETSTANDARD2_0
            timeoutCancellationTokenSource.Cancel();
#endif

#if NET8_0_OR_GREATER
            await timeoutCancellationTokenSource.CancelAsync().ConfigureAwait(false);
#endif

            // disable warning as this is targeted only for web api projects
#pragma warning disable VSTHRD003
            // Very important in order to propagate exceptions
            return await task.ConfigureAwait(false);
#pragma warning restore VSTHRD003
        }
    }
}
