using System;
using System.Threading;
using System.Threading.Tasks;

namespace AdaskoTheBeAsT.Interop.Threading;

internal static class TaskWait
{
    // The monitor never awaits the source outcome: an OCE in a faulted source
    // must stay faulted instead of being reclassified by an async method builder.
    public static Task<T> StartAsync<T>(
        Task source, TimeSpan timeout, Func<Task, T> getResult, Action? onWaitAbandoned, CancellationToken token)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = MonitorAsync(source, timeout, getResult, completion, onWaitAbandoned, token);
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

    private static async Task MonitorAsync<T>(
        Task source,
        TimeSpan timeout,
        Func<Task, T> getResult,
        TaskCompletionSource<T> completion,
        Action? onWaitAbandoned,
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
