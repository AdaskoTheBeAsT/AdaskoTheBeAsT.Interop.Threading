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
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="timeout"/> is negative or exceeds the range supported by <see cref="Task.Delay(TimeSpan, CancellationToken)"/>.</exception>
    public static Task<TResult> TimeoutAfterAsync<TResult>(
        this Task<TResult> task,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        try
        {
            Validate(task, timeout);
        }
        catch (ArgumentException ex)
        {
            // Preserve the existing asynchronous validation timing.
            return Task.FromException<TResult>(ex);
        }

        return task.IsCompleted || (timeout == Timeout.InfiniteTimeSpan && !cancellationToken.CanBeCanceled)
            ? task
            : TaskWait.Start(task, timeout, cancellationToken, static source => ((Task<TResult>)source).GetAwaiter().GetResult());
    }

    /// <summary>
    /// Waits for a task without canceling or taking ownership of that task.
    /// A completed source wins over cancellation of the wait. Validation always runs first.
    /// </summary>
    /// <param name="task">The source task.</param>
    /// <param name="timeout">The wait budget, or <see cref="Timeout.InfiniteTimeSpan"/>.</param>
    /// <param name="cancellationToken">Cancels only the wait.</param>
    /// <returns>A task preserving the source outcome, or reporting wait timeout/cancellation.</returns>
    public static Task TimeoutAfterAsync(this Task task, TimeSpan timeout, CancellationToken cancellationToken)
    {
        try
        {
            Validate(task, timeout);
        }
        catch (ArgumentException ex)
        {
            return Task.FromException(ex);
        }

        return task.IsCompleted || (timeout == Timeout.InfiniteTimeSpan && !cancellationToken.CanBeCanceled)
            ? task
            : TaskWait.Start(task, timeout, cancellationToken, static _ => true);
    }

    private static void Validate(Task task, TimeSpan timeout)
    {
#if NET8_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(task);
#else
        if (task is null)
        {
            throw new ArgumentNullException(nameof(task));
        }
#endif
        TimeoutValidation.Validate(timeout, nameof(timeout));
    }
}
