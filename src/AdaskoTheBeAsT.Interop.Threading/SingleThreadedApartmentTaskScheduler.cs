using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace AdaskoTheBeAsT.Interop.Threading;

/// <summary>
/// Queues delegates onto one reusable background STA thread with an OLE message loop.
/// This scheduler is process-wide and serializes all queued work items onto the same STA thread.
/// </summary>
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

    /// <summary>
    /// Queues a delegate onto the shared STA scheduler and provides a <see cref="StaYield"/> helper for cooperative message pumping.
    /// </summary>
    /// <typeparam name="T">The type returned by the delegate.</typeparam>
    /// <param name="work">The delegate to execute on the shared STA thread.</param>
    /// <param name="cancellationToken">A token that can cancel the queued operation before it starts.</param>
    /// <returns>A task that completes with the delegate result, faults with the original exception, or is canceled.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="work"/> is <see langword="null"/>.</exception>
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

    /// <summary>
    /// Queues an action onto the shared STA scheduler and provides a <see cref="StaYield"/> helper for cooperative message pumping.
    /// </summary>
    /// <param name="work">The action to execute on the shared STA thread.</param>
    /// <param name="cancellationToken">A token that can cancel the queued operation before it starts.</param>
    /// <returns>A task that completes when the action finishes, faults with the original exception, or is canceled.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="work"/> is <see langword="null"/>.</exception>
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

    /// <summary>
    /// Queues a delegate onto the shared STA scheduler.
    /// </summary>
    /// <typeparam name="T">The type returned by the delegate.</typeparam>
    /// <param name="func">The delegate to execute on the shared STA thread.</param>
    /// <param name="cancellationToken">A token that can cancel the queued operation before it starts.</param>
    /// <returns>A task that completes with the delegate result, faults with the original exception, or is canceled.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="func"/> is <see langword="null"/>.</exception>
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

    /// <summary>
    /// Stops the scheduler from accepting new work and signals the background STA thread to shut down.
    /// </summary>
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
