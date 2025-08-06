using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace AdaskoTheBeAsT.Interop.Threading;

public static class SingleThreadedApartmentTaskScheduler
{
    private static readonly ConcurrentQueue<(Delegate Work, TaskCompletionSource<object?> TaskCompletionSource)>
        _queue
            = new();

    private static readonly AutoResetEvent _workAvailable = new(false);

    static SingleThreadedApartmentTaskScheduler()
    {
        var thread = new Thread(MessageLoopThread)
        {
            IsBackground = true,
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    public static Task<T?> RunAsync<T>(Func<T?> func, CancellationToken cancellationToken)
    {
        if (func is null)
        {
            throw new ArgumentNullException(nameof(func));
        }

        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _queue.Enqueue((func, tcs));
        _workAvailable.Set();
        return tcs.Task.ContinueWith(
            t =>
                (T?)t.Result,
            cancellationToken,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private static void MessageLoopThread()
    {
        // We wait on exactly one handle (the AutoResetEvent) + the message queue.
#pragma warning disable S3869
#pragma warning disable CC0001
        IntPtr[] handles = [_workAvailable.SafeWaitHandle.DangerousGetHandle()];
#pragma warning restore CC0001
#pragma warning restore S3869

        while (true)
        {
            // Wait for either: a) workAvailable signaled, or b) a Windows message
            var result = NativeMethods.MsgWaitForMultipleObjects(
                nCount: (uint)handles.Length,
                pHandles: handles,
                bWaitAll: false,
                dwMilliseconds: NativeMethods.INFINITE,
                dwWakeMask: NativeMethods.QS_ALLINPUT);

            if (result == NativeMethods.WAIT_OBJECT_0)
            {
                // The work-available event fired: process exactly one queued item
                if (_queue.TryDequeue(out var item))
                {
                    try
                    {
                        var res = item.Work.DynamicInvoke();
                        item.TaskCompletionSource.SetResult(res);
                    }
                    catch (Exception ex)
                    {
                        item.TaskCompletionSource.SetException(ex);
                    }
                }
            }
            else if (result == NativeMethods.WAIT_OBJECT_0 + handles.Length)
            {
                NativeMethods.PumpPendingMessages();
            }
            else
            {
                // WAIT_FAILED or bizarre error—break or log
                break;
            }
        }
    }
}
