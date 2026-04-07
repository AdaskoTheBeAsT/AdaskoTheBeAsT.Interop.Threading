using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace AdaskoTheBeAsT.Interop.Threading;

public static class SingleThreadedApartmentTaskScheduler
{
    private static readonly ConcurrentQueue<IStaWorkItem> _queue = new();
    private static readonly AutoResetEvent _workAvailable = new(false);
    private static readonly ManualResetEvent _shutdownEvent = new(false);
    private static volatile bool _isShuttingDown;

    static SingleThreadedApartmentTaskScheduler()
    {
        var thread = new Thread(() =>
        {
            var hr = NativeMethods.OleInitialize(IntPtr.Zero);
            if (hr < 0)
            {
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

        AppDomain.CurrentDomain.ProcessExit += (_, _) => Shutdown();
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

#pragma warning disable CC0031
        return RunAsync(() => work(new StaYield()), cancellationToken);
#pragma warning restore CC0031
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

#pragma warning disable CC0031
        return RunAsync<object?>(
            () =>
            {
                work(new StaYield());
                return null;
            },
            cancellationToken);
#pragma warning restore CC0031
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

        if (_isShuttingDown)
        {
            return Task.FromException<T?>(new InvalidOperationException("The STA task scheduler is shutting down."));
        }

        var item = new StaWorkItem<T>(func, cancellationToken);
        _queue.Enqueue(item);
        _workAvailable.Set();
        return item.Task;
    }

    public static void Shutdown()
    {
        if (_isShuttingDown)
        {
            return;
        }

        _isShuttingDown = true;
        _shutdownEvent.Set();
    }

    private static void MessageLoopThread()
    {
#pragma warning disable S3869
#pragma warning disable CC0001
        IntPtr[] handles = [_workAvailable.SafeWaitHandle.DangerousGetHandle(), _shutdownEvent.SafeWaitHandle.DangerousGetHandle()];
#pragma warning restore CC0001
#pragma warning restore S3869

        while (!_isShuttingDown)
        {
            var result = NativeMethods.MsgWaitForMultipleObjects(
                nCount: (uint)handles.Length,
                pHandles: handles,
                bWaitAll: false,
                dwMilliseconds: NativeMethods.INFINITE,
                dwWakeMask: NativeMethods.QS_ALLINPUT);

            if (result == NativeMethods.WAIT_OBJECT_0)
            {
                ProcessQueuedItems();
            }
            else if (result == NativeMethods.WAIT_OBJECT_0 + 1)
            {
                break;
            }
            else if (result == NativeMethods.WAIT_OBJECT_0 + handles.Length)
            {
                NativeMethods.PumpPendingMessages();
            }
            else
            {
                break;
            }
        }

        while (_queue.TryDequeue(out var item))
        {
            item.Cancel();
        }
    }

    private static void ProcessQueuedItems()
    {
        while (_queue.TryDequeue(out var item))
        {
            item.Execute();
            NativeMethods.PumpPendingMessages();
        }
    }
}
