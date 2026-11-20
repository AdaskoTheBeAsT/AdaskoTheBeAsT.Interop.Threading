using System;
using System.Threading;
using System.Threading.Tasks;

namespace AdaskoTheBeAsT.Interop.Threading;

/// <summary>
/// Provides bounded-wait and cooperative linked-deadline timeout helpers for <see cref="Task"/> and (on modern .NET) <c>ValueTask</c>.
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
        CancellationToken cancellationToken = default)
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

#pragma warning disable VSTHRD002
        return task.IsCompleted || (timeout == Timeout.InfiniteTimeSpan && !cancellationToken.CanBeCanceled)
            ? task
            : TaskWait.StartAsync(
                task,
                timeout,
                static source => ((Task<TResult>)source).GetAwaiter().GetResult(),
                onWaitAbandoned: null,
                cancellationToken);
#pragma warning restore VSTHRD002
    }

    /// <summary>
    /// Waits for a task without canceling or taking ownership of that task.
    /// A completed source wins over cancellation of the wait. Validation always runs first.
    /// </summary>
    /// <param name="task">The source task.</param>
    /// <param name="timeout">The wait budget, or <see cref="Timeout.InfiniteTimeSpan"/>.</param>
    /// <param name="cancellationToken">Cancels only the wait.</param>
    /// <returns>A task preserving the source outcome, or reporting wait timeout/cancellation.</returns>
    public static Task TimeoutAfterAsync(
        this Task task,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
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
            : TaskWait.StartAsync(task, timeout, static _ => true, onWaitAbandoned: null, cancellationToken);
    }

    /// <summary>
    /// Invokes a cooperative asynchronous operation with a linked deadline token and bounds the caller's wait.
    /// </summary>
    /// <param name="operation">The delegate receiving a token linked to <paramref name="cancellationToken"/> and <paramref name="timeout"/>.</param>
    /// <param name="timeout">The deadline budget, or <see cref="Timeout.InfiniteTimeSpan"/>.</param>
    /// <param name="cancellationToken">Caller cancellation token.</param>
    /// <returns>A task completing with the operation outcome, <see cref="TimeoutException"/> on deadline expiry, or caller cancellation.</returns>
#if NET9_0_OR_GREATER
    [System.Runtime.CompilerServices.OverloadResolutionPriority(1)]
#endif
    public static Task TimeoutAfterAsync(
        Func<CancellationToken, Task> operation,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        try
        {
            ValidateOperation(operation, timeout);
        }
        catch (ArgumentException ex)
        {
            return Task.FromException(ex);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled(cancellationToken);
        }

        if (timeout == Timeout.InfiniteTimeSpan)
        {
            return InvokeInfiniteTaskAsync(operation, cancellationToken);
        }

        return ExecuteCooperativeTaskAsync(operation, timeout, cancellationToken);
    }

    /// <summary>
    /// Invokes a cooperative asynchronous operation with a linked deadline token and bounds the caller's wait.
    /// </summary>
    /// <typeparam name="TResult">The result type produced by <paramref name="operation"/>.</typeparam>
    /// <param name="operation">The delegate receiving a token linked to <paramref name="cancellationToken"/> and <paramref name="timeout"/>.</param>
    /// <param name="timeout">The deadline budget, or <see cref="Timeout.InfiniteTimeSpan"/>.</param>
    /// <param name="cancellationToken">Caller cancellation token.</param>
    /// <returns>A task completing with the operation result, <see cref="TimeoutException"/> on deadline expiry, or caller cancellation.</returns>
#if NET9_0_OR_GREATER
    [System.Runtime.CompilerServices.OverloadResolutionPriority(1)]
#endif
    public static Task<TResult> TimeoutAfterAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        try
        {
            ValidateOperation(operation, timeout);
        }
        catch (ArgumentException ex)
        {
            return Task.FromException<TResult>(ex);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<TResult>(cancellationToken);
        }

        if (timeout == Timeout.InfiniteTimeSpan)
        {
            return InvokeInfiniteTaskAsync(operation, cancellationToken);
        }

        return ExecuteCooperativeTaskAsync(operation, timeout, cancellationToken);
    }

#if NET8_0_OR_GREATER
    /// <summary>
    /// Waits for a <see cref="ValueTask"/> to complete within <paramref name="timeout"/> while consuming any underlying
    /// <c>IValueTaskSource</c> strictly once and avoiding heap allocation when already completed successfully.
    /// </summary>
    /// <param name="task">The source <see cref="ValueTask"/>.</param>
    /// <param name="timeout">The wait budget, or <see cref="Timeout.InfiniteTimeSpan"/>.</param>
    /// <param name="cancellationToken">Cancels only the wait.</param>
    /// <returns>A <see cref="ValueTask"/> preserving the source outcome, or reporting wait timeout/cancellation.</returns>
    public static ValueTask TimeoutAfterAsync(
        this ValueTask task,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        try
        {
            TimeoutValidation.Validate(timeout, nameof(timeout));
        }
        catch (ArgumentException ex)
        {
            ConsumeOrObserveAbandoned(task);
            return ValueTask.FromException(ex);
        }

        if (task.IsCompletedSuccessfully)
        {
#pragma warning disable VSTHRD002
            task.GetAwaiter().GetResult();
#pragma warning restore VSTHRD002
            return ValueTask.CompletedTask;
        }

        var asTask = task.AsTask();
        if (asTask.IsCompleted || (timeout == Timeout.InfiniteTimeSpan && !cancellationToken.CanBeCanceled))
        {
            return new ValueTask(asTask);
        }

        var monitored = TaskWait.StartAsync(
            asTask,
            timeout,
            static _ => true,
            onWaitAbandoned: null,
            cancellationToken,
            observeFaultOnAbandonment: true);
        return new ValueTask(monitored);
    }

    /// <summary>
    /// Waits for a <see cref="ValueTask{TResult}"/> to complete within <paramref name="timeout"/> while consuming any underlying
    /// <c>IValueTaskSource&lt;TResult&gt;</c> strictly once and avoiding heap allocation when already completed successfully.
    /// </summary>
    /// <typeparam name="TResult">The result type of <paramref name="task"/>.</typeparam>
    /// <param name="task">The source <see cref="ValueTask{TResult}"/>.</param>
    /// <param name="timeout">The wait budget, or <see cref="Timeout.InfiniteTimeSpan"/>.</param>
    /// <param name="cancellationToken">Cancels only the wait.</param>
    /// <returns>A <see cref="ValueTask{TResult}"/> preserving the source outcome, or reporting wait timeout/cancellation.</returns>
    public static ValueTask<TResult> TimeoutAfterAsync<TResult>(
        this ValueTask<TResult> task,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        try
        {
            TimeoutValidation.Validate(timeout, nameof(timeout));
        }
        catch (ArgumentException ex)
        {
            ConsumeOrObserveAbandoned(task);
            return ValueTask.FromException<TResult>(ex);
        }

        if (task.IsCompletedSuccessfully)
        {
#pragma warning disable VSTHRD002
            var result = task.GetAwaiter().GetResult();
#pragma warning restore VSTHRD002
            return new ValueTask<TResult>(result);
        }

        var asTask = task.AsTask();
        if (asTask.IsCompleted || (timeout == Timeout.InfiniteTimeSpan && !cancellationToken.CanBeCanceled))
        {
            return new ValueTask<TResult>(asTask);
        }

#pragma warning disable VSTHRD002
        var monitored = TaskWait.StartAsync(
            asTask,
            timeout,
            static source => ((Task<TResult>)source).GetAwaiter().GetResult(),
            onWaitAbandoned: null,
            cancellationToken,
            observeFaultOnAbandonment: true);
#pragma warning restore VSTHRD002
        return new ValueTask<TResult>(monitored);
    }

    /// <summary>
    /// Invokes a cooperative <see cref="ValueTask"/> operation with a linked deadline token, consuming any pooled
    /// <c>IValueTaskSource</c> strictly once.
    /// </summary>
    /// <param name="operation">The delegate receiving a token linked to <paramref name="cancellationToken"/> and <paramref name="timeout"/>.</param>
    /// <param name="timeout">The deadline budget, or <see cref="Timeout.InfiniteTimeSpan"/>.</param>
    /// <param name="cancellationToken">Caller cancellation token.</param>
    /// <returns>A <see cref="ValueTask"/> completing with the operation outcome, <see cref="TimeoutException"/> on deadline expiry, or caller cancellation.</returns>
    public static ValueTask TimeoutAfterAsync(
        Func<CancellationToken, ValueTask> operation,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        try
        {
            ValidateOperation(operation, timeout);
        }
        catch (ArgumentException ex)
        {
            return ValueTask.FromException(ex);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return ValueTask.FromCanceled(cancellationToken);
        }

        if (timeout == Timeout.InfiniteTimeSpan)
        {
            return InvokeInfiniteValueTaskAsync(operation, cancellationToken);
        }

        return ExecuteCooperativeValueTaskAsync(operation, timeout, cancellationToken);
    }

    /// <summary>
    /// Invokes a cooperative <see cref="ValueTask{TResult}"/> operation with a linked deadline token, consuming any pooled
    /// <c>IValueTaskSource&lt;TResult&gt;</c> strictly once.
    /// </summary>
    /// <typeparam name="TResult">The result type produced by <paramref name="operation"/>.</typeparam>
    /// <param name="operation">The delegate receiving a token linked to <paramref name="cancellationToken"/> and <paramref name="timeout"/>.</param>
    /// <param name="timeout">The deadline budget, or <see cref="Timeout.InfiniteTimeSpan"/>.</param>
    /// <param name="cancellationToken">Caller cancellation token.</param>
    /// <returns>A <see cref="ValueTask{TResult}"/> completing with the operation result, <see cref="TimeoutException"/> on deadline expiry, or caller cancellation.</returns>
    public static ValueTask<TResult> TimeoutAfterAsync<TResult>(
        Func<CancellationToken, ValueTask<TResult>> operation,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        try
        {
            ValidateOperation(operation, timeout);
        }
        catch (ArgumentException ex)
        {
            return ValueTask.FromException<TResult>(ex);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return ValueTask.FromCanceled<TResult>(cancellationToken);
        }

        if (timeout == Timeout.InfiniteTimeSpan)
        {
            return InvokeInfiniteValueTaskAsync(operation, cancellationToken);
        }

        return ExecuteCooperativeValueTaskAsync(operation, timeout, cancellationToken);
    }
#endif

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

    private static void ValidateOperation(Delegate operation, TimeSpan timeout)
    {
#if NET8_0_OR_GREATER
#pragma warning disable S3236 // Preserve the public parameter name through this shared guard.
        ArgumentNullException.ThrowIfNull(operation, nameof(operation));
#pragma warning restore S3236
#else
        if (operation is null)
        {
            throw new ArgumentNullException(nameof(operation));
        }
#endif
        TimeoutValidation.Validate(timeout, nameof(timeout));
    }

#pragma warning disable CC0031 // Validated by ValidateOperation before invocation.
    private static Task InvokeInfiniteTaskAsync(Func<CancellationToken, Task> operation, CancellationToken token)
    {
        try
        {
            var pending = operation(token);
            return pending ?? Task.FromException(new InvalidOperationException("The timeout operation delegate returned a null Task."));
        }
        catch (OperationCanceledException oce) when (token.IsCancellationRequested
                                                     && (oce.CancellationToken == token || !oce.CancellationToken.CanBeCanceled))
        {
            return Task.FromCanceled(token);
        }
        catch (Exception ex)
        {
            return Task.FromException(ex);
        }
    }

    private static Task<TResult> InvokeInfiniteTaskAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken token)
    {
        try
        {
            var pending = operation(token);
            return pending ?? Task.FromException<TResult>(new InvalidOperationException("The timeout operation delegate returned a null Task."));
        }
        catch (OperationCanceledException oce) when (token.IsCancellationRequested
                                                     && (oce.CancellationToken == token || !oce.CancellationToken.CanBeCanceled))
        {
            return Task.FromCanceled<TResult>(token);
        }
        catch (Exception ex)
        {
            return Task.FromException<TResult>(ex);
        }
    }

    private static void ArmDeadline(CancellationTokenSource deadline, TimeSpan timeout)
    {
        if (timeout == TimeSpan.Zero)
        {
            deadline.Cancel();
        }
        else
        {
            deadline.CancelAfter(timeout);
        }
    }

#pragma warning disable CA2000, IDISP001 // Ownership of deadline transfers to CompleteCooperativeTaskAsync / TaskWait.StartCooperativeAsync.
    private static Task ExecuteCooperativeTaskAsync(
        Func<CancellationToken, Task> operation,
        TimeSpan timeout,
        CancellationToken callerToken)
    {
        var deadline = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
        try
        {
            ArmDeadline(deadline, timeout);
            var pending = operation(deadline.Token)
                ?? throw new InvalidOperationException("The timeout operation delegate returned a null Task.");
            return CompleteCooperativeTaskAsync(pending, deadline, timeout, callerToken);
        }
        catch (Exception ex)
        {
            return FailAndDisposeDeadlineAsync(ex, timeout, callerToken, deadline);
        }
    }

    private static Task<TResult> ExecuteCooperativeTaskAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        TimeSpan timeout,
        CancellationToken callerToken)
    {
        var deadline = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
        try
        {
            ArmDeadline(deadline, timeout);
            var pending = operation(deadline.Token)
                ?? throw new InvalidOperationException("The timeout operation delegate returned a null Task.");
            return CompleteCooperativeTaskAsync(pending, deadline, timeout, callerToken);
        }
        catch (Exception ex)
        {
            return FailAndDisposeDeadlineAsync<TResult>(ex, timeout, callerToken, deadline);
        }
    }
#pragma warning restore CA2000, IDISP001

#pragma warning disable IDISP007, VSTHRD003 // Ownership of deadline belongs to this completion path; returning completed pending task is intentional.
    private static Task CompleteCooperativeTaskAsync(
        Task pending,
        CancellationTokenSource deadline,
        TimeSpan timeout,
        CancellationToken callerToken)
    {
        if (pending.Status == TaskStatus.RanToCompletion)
        {
            deadline.Dispose();
            return pending;
        }

        return TaskWait.StartCooperativeAsync(pending, deadline, timeout, static _ => true, callerToken);
    }

    private static Task<TResult> CompleteCooperativeTaskAsync<TResult>(
        Task<TResult> pending,
        CancellationTokenSource deadline,
        TimeSpan timeout,
        CancellationToken callerToken)
    {
        if (pending.Status == TaskStatus.RanToCompletion)
        {
            deadline.Dispose();
            return pending;
        }

#pragma warning disable VSTHRD002
        return TaskWait.StartCooperativeAsync(
            pending,
            deadline,
            timeout,
            static source => ((Task<TResult>)source).GetAwaiter().GetResult(),
            callerToken);
#pragma warning restore VSTHRD002
    }

    private static Task FailAndDisposeDeadlineAsync(
        Exception ex,
        TimeSpan timeout,
        CancellationToken callerToken,
        CancellationTokenSource deadline)
    {
        var deadlineToken = deadline.Token;
        var deadlineCanceled = deadline.IsCancellationRequested;
        deadline.Dispose();
        if (TaskWait.TryClassifySyncCooperativeException(
                ex,
                timeout,
                callerToken,
                deadlineToken,
                deadlineCanceled,
                out var classifiedFailure,
                out var isCallerCancellation))
        {
            return isCallerCancellation
                ? Task.FromCanceled(callerToken)
                : Task.FromException(classifiedFailure!);
        }

        return Task.FromException(ex);
    }

    private static Task<TResult> FailAndDisposeDeadlineAsync<TResult>(
        Exception ex,
        TimeSpan timeout,
        CancellationToken callerToken,
        CancellationTokenSource deadline)
    {
        var deadlineToken = deadline.Token;
        var deadlineCanceled = deadline.IsCancellationRequested;
        deadline.Dispose();
        if (TaskWait.TryClassifySyncCooperativeException(
                ex,
                timeout,
                callerToken,
                deadlineToken,
                deadlineCanceled,
                out var classifiedFailure,
                out var isCallerCancellation))
        {
            return isCallerCancellation
                ? Task.FromCanceled<TResult>(callerToken)
                : Task.FromException<TResult>(classifiedFailure!);
        }

        return Task.FromException<TResult>(ex);
    }
#pragma warning restore IDISP007, VSTHRD003

#if NET8_0_OR_GREATER
    private static void ConsumeOrObserveAbandoned(ValueTask task)
    {
        if (task.IsCompletedSuccessfully)
        {
#pragma warning disable VSTHRD002
            task.GetAwaiter().GetResult();
#pragma warning restore VSTHRD002
        }
        else
        {
            TaskWait.ObserveFault(task.AsTask());
        }
    }

    private static void ConsumeOrObserveAbandoned<TResult>(ValueTask<TResult> task)
    {
        if (task.IsCompletedSuccessfully)
        {
#pragma warning disable VSTHRD002
            _ = task.GetAwaiter().GetResult();
#pragma warning restore VSTHRD002
        }
        else
        {
            TaskWait.ObserveFault(task.AsTask());
        }
    }

    private static ValueTask InvokeInfiniteValueTaskAsync(Func<CancellationToken, ValueTask> operation, CancellationToken token)
    {
        try
        {
            var pending = operation(token);
            if (pending.IsCompletedSuccessfully)
            {
#pragma warning disable VSTHRD002
                pending.GetAwaiter().GetResult();
#pragma warning restore VSTHRD002
                return ValueTask.CompletedTask;
            }

            return new ValueTask(pending.AsTask());
        }
        catch (OperationCanceledException oce) when (token.IsCancellationRequested
                                                     && (oce.CancellationToken == token || !oce.CancellationToken.CanBeCanceled))
        {
            return ValueTask.FromCanceled(token);
        }
        catch (Exception ex)
        {
            return ValueTask.FromException(ex);
        }
    }

    private static ValueTask<TResult> InvokeInfiniteValueTaskAsync<TResult>(
        Func<CancellationToken, ValueTask<TResult>> operation,
        CancellationToken token)
    {
        try
        {
            var pending = operation(token);
            if (pending.IsCompletedSuccessfully)
            {
#pragma warning disable VSTHRD002
                var result = pending.GetAwaiter().GetResult();
#pragma warning restore VSTHRD002
                return new ValueTask<TResult>(result);
            }

            return new ValueTask<TResult>(pending.AsTask());
        }
        catch (OperationCanceledException oce) when (token.IsCancellationRequested
                                                     && (oce.CancellationToken == token || !oce.CancellationToken.CanBeCanceled))
        {
            return ValueTask.FromCanceled<TResult>(token);
        }
        catch (Exception ex)
        {
            return ValueTask.FromException<TResult>(ex);
        }
    }

#pragma warning disable CA2000, IDISP001, IDISP007 // Ownership of deadline transfers to CompleteCooperativeValueTaskAsync / FailAndDisposeDeadlineAsync.
    private static ValueTask ExecuteCooperativeValueTaskAsync(
        Func<CancellationToken, ValueTask> operation,
        TimeSpan timeout,
        CancellationToken callerToken)
    {
        var deadline = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
        try
        {
            ArmDeadline(deadline, timeout);
            var vt = operation(deadline.Token);
            return CompleteCooperativeValueTaskAsync(vt, deadline, timeout, callerToken);
        }
        catch (Exception ex)
        {
            return new ValueTask(FailAndDisposeDeadlineAsync(ex, timeout, callerToken, deadline));
        }
    }

    private static ValueTask<TResult> ExecuteCooperativeValueTaskAsync<TResult>(
        Func<CancellationToken, ValueTask<TResult>> operation,
        TimeSpan timeout,
        CancellationToken callerToken)
    {
        var deadline = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
        try
        {
            ArmDeadline(deadline, timeout);
            var vt = operation(deadline.Token);
            return CompleteCooperativeValueTaskAsync(vt, deadline, timeout, callerToken);
        }
        catch (Exception ex)
        {
            return new ValueTask<TResult>(FailAndDisposeDeadlineAsync<TResult>(ex, timeout, callerToken, deadline));
        }
    }

    private static ValueTask CompleteCooperativeValueTaskAsync(
        ValueTask vt,
        CancellationTokenSource deadline,
        TimeSpan timeout,
        CancellationToken callerToken)
    {
        if (vt.IsCompletedSuccessfully)
        {
#pragma warning disable VSTHRD002
            vt.GetAwaiter().GetResult();
#pragma warning restore VSTHRD002
            deadline.Dispose();
            return ValueTask.CompletedTask;
        }

        return new ValueTask(CompleteCooperativeTaskAsync(vt.AsTask(), deadline, timeout, callerToken));
    }

    private static ValueTask<TResult> CompleteCooperativeValueTaskAsync<TResult>(
        ValueTask<TResult> vt,
        CancellationTokenSource deadline,
        TimeSpan timeout,
        CancellationToken callerToken)
    {
        if (vt.IsCompletedSuccessfully)
        {
#pragma warning disable VSTHRD002
            var result = vt.GetAwaiter().GetResult();
#pragma warning restore VSTHRD002
            deadline.Dispose();
            return new ValueTask<TResult>(result);
        }

        return new ValueTask<TResult>(CompleteCooperativeTaskAsync(vt.AsTask(), deadline, timeout, callerToken));
    }
#pragma warning restore CA2000, IDISP001, IDISP007
#endif
#pragma warning restore CC0031
}
