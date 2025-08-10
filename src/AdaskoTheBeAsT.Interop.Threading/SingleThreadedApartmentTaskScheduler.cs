using System;
using System.Collections.Concurrent;
using System.Diagnostics;
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
        var thread = new Thread(() =>
        {
            var hr = NativeMethods.OleInitialize(IntPtr.Zero);
            if (hr < 0)
            {
                // Optionally, handle the error (throw, log, etc.)
                // For now, just throw an exception to make the error visible.
                Debug.WriteLine($"OleInitialize failed with HRESULT: 0x{hr:X8}");
                return;
            }

            try
            {
                MessageLoopThread();
            }
            finally
            {
                NativeMethods.OleUninitialize();
            }
        })
        {
            IsBackground = true,
            Name = "STA Task Scheduler Thread",
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    public static Task<T?> RunAsync<T>(Func<StaYield, T?> work, CancellationToken cancellationToken = default)
    {
        if (work is null)
        {
            throw new ArgumentNullException(nameof(work));
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<T?>(cancellationToken);
        }

        return RunAsync(() => work!(new StaYield()), cancellationToken);
    }

    public static Task RunAsync(Action<StaYield> work, CancellationToken cancellationToken = default)
    {
        if (work is null)
        {
            throw new ArgumentNullException(nameof(work));
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled(cancellationToken);
        }

        return RunAsync<object?>(
            () =>
            {
                work!(new StaYield());
                return null;
            },
            cancellationToken);
    }

    public static Task<T?> RunAsync<T>(Func<T?> func, CancellationToken cancellationToken)
    {
        if (func is null)
        {
            throw new ArgumentNullException(nameof(func));
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<T?>(cancellationToken);
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
                        item.TaskCompletionSource.TrySetResult(res);
                    }
                    catch (Exception ex)
                    {
                        item.TaskCompletionSource.SetException(ex);
                    }

                    NativeMethods.PumpPendingMessages();
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
