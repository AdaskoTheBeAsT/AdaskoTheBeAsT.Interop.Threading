using System;
using System.Threading;
using System.Threading.Tasks;

namespace AdaskoTheBeAsT.Interop.Threading;

internal static class TaskWait
{
    // The monitor never awaits the source outcome: an OCE in a faulted source
    // must stay faulted instead of being reclassified by an async method builder.
    public static Task<T> StartAsync<T>(
        Task source,
        TimeSpan timeout,
        Func<Task, T> getResult,
        Action? onWaitAbandoned,
        CancellationToken token,
        bool observeFaultOnAbandonment = false)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = MonitorAsync(source, timeout, getResult, completion, onWaitAbandoned, observeFaultOnAbandonment, token);
        return completion.Task;
    }

    public static Task<T> StartCooperativeAsync<T>(
        Task source,
        CancellationTokenSource deadline,
        TimeSpan timeout,
        Func<Task, T> getResult,
        CancellationToken callerToken)
    {
        if (source.IsCompleted)
        {
            var deadlineToken = deadline.Token;
            var deadlineCanceled = deadline.IsCancellationRequested;
#pragma warning disable IDISP007 // Ownership of deadline transfers here once source has settled.
            deadline.Dispose();
#pragma warning restore IDISP007
            var syncCompletion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            TransferCooperative(source, syncCompletion, getResult, timeout, callerToken, deadlineToken, deadlineCanceled);
            return syncCompletion.Task;
        }

        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = MonitorCooperativeAsync(source, deadline, timeout, getResult, completion, callerToken);
        return completion.Task;
    }

    public static void ObserveFault(Task task)
    {
        _ = task.ContinueWith(
            static completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    public static void Transfer<T>(Task source, TaskCompletionSource<T> target, Func<Task, T> getResult)
    {
        if (source.Exception is { } exception)
        {
            target.TrySetException(exception.InnerExceptions);
        }
        else if (source.IsCanceled)
        {
            try
            {
#pragma warning disable VSTHRD002
                source.GetAwaiter().GetResult();
#pragma warning restore VSTHRD002
            }
            catch (OperationCanceledException ex)
            {
                target.TrySetCanceled(ex.CancellationToken);
            }
        }
        else
        {
            // Only internally supplied, non-null result extractors reach this path.
#pragma warning disable CC0031
            target.TrySetResult(getResult(source));
#pragma warning restore CC0031
        }
    }

    public static bool TryClassifySyncCooperativeException(
        Exception exception,
        TimeSpan timeout,
        CancellationToken callerToken,
        CancellationToken deadlineToken,
        bool deadlineCanceled,
        out Exception? classifiedFailure,
        out bool isCallerCancellation)
    {
        classifiedFailure = null;
        isCallerCancellation = false;
        if (exception is not OperationCanceledException oce)
        {
            return false;
        }

        if (callerToken.IsCancellationRequested
            && (oce.CancellationToken == deadlineToken
                || oce.CancellationToken == callerToken
                || !oce.CancellationToken.CanBeCanceled))
        {
            isCallerCancellation = true;
            return true;
        }

        if (deadlineCanceled
            && (oce.CancellationToken == deadlineToken || !oce.CancellationToken.CanBeCanceled))
        {
            classifiedFailure = new TimeoutException($"The operation has timed out after {timeout}.");
            return true;
        }

        return false;
    }

    private static void TransferCooperative<T>(
        Task source,
        TaskCompletionSource<T> target,
        Func<Task, T> getResult,
        TimeSpan timeout,
        CancellationToken callerToken,
        CancellationToken deadlineToken,
        bool deadlineCanceled)
    {
        if (source.Status == TaskStatus.RanToCompletion)
        {
#pragma warning disable CC0031
            target.TrySetResult(getResult(source));
#pragma warning restore CC0031
            return;
        }

        if (TryGetCooperativeCancellation(source, callerToken, deadlineToken, deadlineCanceled, out var isCallerCancellation))
        {
            if (isCallerCancellation)
            {
                target.TrySetCanceled(callerToken);
            }
            else
            {
                target.TrySetException(new TimeoutException($"The operation has timed out after {timeout}."));
            }

            return;
        }

        Transfer(source, target, getResult);
    }

    private static bool TryGetCooperativeCancellation(
        Task source,
        CancellationToken callerToken,
        CancellationToken deadlineToken,
        bool deadlineCanceled,
        out bool isCallerCancellation)
    {
        isCallerCancellation = false;
        if (source.IsCanceled)
        {
            var canceledToken = ExtractCanceledToken(source);
            if (callerToken.IsCancellationRequested
                && (canceledToken == deadlineToken || canceledToken == callerToken || !canceledToken.CanBeCanceled))
            {
                isCallerCancellation = true;
                return true;
            }

            if (deadlineCanceled && (canceledToken == deadlineToken || !canceledToken.CanBeCanceled))
            {
                return true;
            }

            return false;
        }

        if (source.Exception is { InnerExceptions.Count: 1 } aggregate
            && aggregate.InnerExceptions[0] is OperationCanceledException faultedOce)
        {
            if (callerToken.IsCancellationRequested
                && (faultedOce.CancellationToken == deadlineToken
                    || (callerToken.CanBeCanceled && faultedOce.CancellationToken == callerToken)))
            {
                isCallerCancellation = true;
                return true;
            }

            if (deadlineCanceled && faultedOce.CancellationToken == deadlineToken)
            {
                return true;
            }
        }

        return false;
    }

    private static CancellationToken ExtractCanceledToken(Task source)
    {
        try
        {
#pragma warning disable VSTHRD002
            source.GetAwaiter().GetResult();
#pragma warning restore VSTHRD002
        }
        catch (OperationCanceledException ex)
        {
            return ex.CancellationToken;
        }

        return CancellationToken.None;
    }

    private static void DisposeWhenSettled(Task source, CancellationTokenSource deadline)
    {
        _ = source.ContinueWith(
            static (_, state) =>
            {
                if (state is CancellationTokenSource cts)
                {
                    cts.Dispose();
                }
            },
            deadline,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private static async Task MonitorCooperativeAsync<T>(
        Task source,
        CancellationTokenSource deadline,
        TimeSpan timeout,
        Func<Task, T> getResult,
        TaskCompletionSource<T> completion,
        CancellationToken callerToken)
    {
        var deadlineToken = deadline.Token;
        Task winner;
        using (var waitCts = CancellationTokenSource.CreateLinkedTokenSource(deadlineToken))
        {
            try
            {
                var delay = Task.Delay(Timeout.InfiniteTimeSpan, waitCts.Token);
#pragma warning disable VSTHRD003
                winner = await Task.WhenAny(source, delay).ConfigureAwait(false);
#pragma warning restore VSTHRD003
            }
            finally
            {
#if NET8_0_OR_GREATER
                await waitCts.CancelAsync().ConfigureAwait(false);
#else
                waitCts.Cancel();
#endif
            }
        }

        if (winner == source)
        {
            var deadlineCanceled = deadline.IsCancellationRequested;
#pragma warning disable IDISP007 // Ownership of deadline transfers here once source settles.
            deadline.Dispose();
#pragma warning restore IDISP007
            TransferCooperative(source, completion, getResult, timeout, callerToken, deadlineToken, deadlineCanceled);
        }
        else
        {
            ObserveFault(source);
            DisposeWhenSettled(source, deadline);
            if (callerToken.IsCancellationRequested)
            {
                completion.TrySetCanceled(callerToken);
            }
            else
            {
                completion.TrySetException(new TimeoutException($"The operation has timed out after {timeout}."));
            }
        }
    }

    private static async Task MonitorAsync<T>(
        Task source,
        TimeSpan timeout,
        Func<Task, T> getResult,
        TaskCompletionSource<T> completion,
        Action? onWaitAbandoned,
        bool observeFaultOnAbandonment,
        CancellationToken token)
    {
        Task winner;
        using (var delayCts = CancellationTokenSource.CreateLinkedTokenSource(token))
        {
            try
            {
                var delay = Task.Delay(timeout, delayCts.Token);
#pragma warning disable VSTHRD003
                winner = await Task.WhenAny(source, delay).ConfigureAwait(false);
#pragma warning restore VSTHRD003
            }
            finally
            {
                // Only Task.Delay sees this token. No caller code executes here.
#if NET8_0_OR_GREATER
                await delayCts.CancelAsync().ConfigureAwait(false);
#else
                delayCts.Cancel();
#endif
            }
        }

        if (winner == source)
        {
            Transfer(source, completion, getResult);
        }
        else
        {
            if (observeFaultOnAbandonment)
            {
                ObserveFault(source);
            }

            onWaitAbandoned?.Invoke();
            if (token.IsCancellationRequested)
            {
                completion.TrySetCanceled(token);
            }
            else
            {
                completion.TrySetException(new TimeoutException($"The operation has timed out after {timeout}."));
            }
        }
    }
}
