using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace AdaskoTheBeAsT.Interop.Threading;

/// <summary>
/// Queues delegates onto a dedicated background STA thread with an OLE message loop.
/// Each instance owns its own STA thread and serializes all queued work items onto it.
/// Dispose the instance to stop the thread deterministically.
/// </summary>
public sealed class SingleThreadedApartmentTaskScheduler : ISingleThreadedApartmentTaskScheduler
{
    private readonly ConcurrentQueue<IStaWorkItem> _queue = new();
    private readonly AutoResetEvent _workAvailable = new(false);
    private readonly ManualResetEvent _shutdownEvent = new(false);
    private readonly CancellationTokenSource _shutdownCts = new();
    private readonly Thread _thread;
    private readonly TimeSpan _defaultTimeout;
    private readonly TaskCompletionSource<bool> _threadReady =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private volatile bool _isShuttingDown;
    private volatile bool _isDisposed;
    private int _oleInitResult;

    /// <summary>
    /// Initializes a new instance of the <see cref="SingleThreadedApartmentTaskScheduler"/> class
    /// using default options and starts a dedicated STA background thread that runs an OLE message loop.
    /// </summary>
    public SingleThreadedApartmentTaskScheduler()
        : this(new SingleThreadedApartmentTaskSchedulerOptions())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SingleThreadedApartmentTaskScheduler"/> class
    /// with the supplied options and starts a dedicated STA background thread that runs an OLE message loop.
    /// </summary>
    /// <param name="options">Options controlling scheduler behavior. When <see langword="null"/>, defaults are used.</param>
    public SingleThreadedApartmentTaskScheduler(SingleThreadedApartmentTaskSchedulerOptions? options)
    {
        var effectiveOptions = options ?? new SingleThreadedApartmentTaskSchedulerOptions();
        var threadName = string.IsNullOrWhiteSpace(effectiveOptions.ThreadName)
            ? SingleThreadedApartmentTaskSchedulerOptions.DefaultThreadName
            : effectiveOptions.ThreadName;
        _defaultTimeout = effectiveOptions.DefaultWorkItemTimeout;

        _thread = new Thread(ThreadEntry)
        {
            IsBackground = true,
            Name = threadName,
        };

        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    /// <inheritdoc />
    public Task<T?> RunAsync<T>(Func<StaYield, T?> work, CancellationToken cancellationToken = default)
    {
        if (work is null)
        {
            throw new ArgumentNullException(nameof(work));
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<T?>(cancellationToken);
        }

        // Intentional closure allocation: exposes StaYield to the caller's delegate.
#pragma warning disable CC0031
        return RunAsync(() => work(new StaYield()), cancellationToken);
#pragma warning restore CC0031
    }

    /// <inheritdoc />
    public Task RunAsync(Action<StaYield> work, CancellationToken cancellationToken = default)
    {
        if (work is null)
        {
            throw new ArgumentNullException(nameof(work));
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled(cancellationToken);
        }

        // Intentional closure allocation: wraps Action<StaYield> into Func<object?>.
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

    /// <inheritdoc />
    public Task<T?> RunAsync<T>(Func<T?> func, CancellationToken cancellationToken)
    {
        return RunAsync(func, _defaultTimeout, cancellationToken);
    }

    /// <inheritdoc />
    public Task<T?> RunAsync<T>(Func<T?> func, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (func is null)
        {
            throw new ArgumentNullException(nameof(func));
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<T?>(cancellationToken);
        }

        if (_isDisposed)
        {
            return Task.FromException<T?>(new ObjectDisposedException(nameof(SingleThreadedApartmentTaskScheduler)));
        }

        if (_isShuttingDown)
        {
            return Task.FromException<T?>(new InvalidOperationException("The STA task scheduler is shutting down."));
        }

        if (_oleInitResult < 0)
        {
            return Task.FromException<T?>(
                new InvalidOperationException(
                    $"The STA task scheduler thread failed to initialize OLE (HRESULT: 0x{_oleInitResult:X8})."));
        }

        return EnqueueAndOptionallyTimeoutAsync(func, timeout, cancellationToken);
    }

    /// <inheritdoc />
    public void Shutdown()
    {
        if (_isShuttingDown)
        {
            return;
        }

        _isShuttingDown = true;

        // Signal cooperative cancellation to any work currently running on the STA thread
        // (the running item observes the shared shutdown token) and to any items queued
        // but not yet dequeued.
#pragma warning disable CA1031
        try
        {
            _shutdownCts.Cancel();
        }
        catch (ObjectDisposedException ex)
        {
            // Disposed concurrently; safe to ignore.
            Debug.WriteLine($"Shutdown CTS was disposed concurrently: {ex.Message}");
        }
#pragma warning restore CA1031

        _shutdownEvent.Set();
    }

    /// <summary>
    /// Stops the scheduler, waits for the background STA thread to exit, and releases underlying synchronization resources.
    /// </summary>
    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        Shutdown();

#pragma warning disable CA1031
        try
        {
            if (_thread.IsAlive && _thread != Thread.CurrentThread)
            {
                _thread.Join();
            }
        }
        catch (Exception ex)
        {
            // best-effort join; never throw from Dispose
            Debug.WriteLine($"STA scheduler thread join failed: {ex}");
        }
#pragma warning restore CA1031

        _workAvailable.Dispose();
        _shutdownEvent.Dispose();
        _shutdownCts.Dispose();
    }

    // CA2000 / IDISP001: the linked CTS ownership transfers to the ContinueWith
    // continuation below, which disposes it when the work item reaches a terminal
    // state. VSTHRD110 / MA0134: the continuation result is intentionally not awaited
    // because its sole purpose is resource cleanup and it never throws.
#pragma warning disable CA2000, IDISP001, VSTHRD110, MA0134
    private Task<T?> EnqueueAndOptionallyTimeoutAsync<T>(
        Func<T?> func,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        // Link the caller token with the scheduler's shutdown token so that a
        // pending work item is canceled if the scheduler is disposed before it runs,
        // and a running work item observes cancellation cooperatively.
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdownCts.Token);
        var item = new StaWorkItem<T>(func, linkedCts.Token);
        var itemTask = item.Task;

        _queue.Enqueue(item);
        _workAvailable.Set();

        // Clean up the linked CTS once the item completes (in any terminal state).
        itemTask.ContinueWith(
            static (_, state) => ((CancellationTokenSource)state!).Dispose(),
            linkedCts,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        if (timeout == Timeout.InfiniteTimeSpan)
        {
            return itemTask;
        }

        return itemTask.TimeoutAfterAsync(timeout, cancellationToken);
    }
#pragma warning restore CA2000, IDISP001, VSTHRD110, MA0134

    private void ThreadEntry()
    {
        _oleInitResult = NativeMethods.OleInitialize(IntPtr.Zero);
        if (_oleInitResult < 0)
        {
            Debug.WriteLine($"OleInitialize failed with HRESULT: 0x{_oleInitResult:X8}");
            _threadReady.TrySetResult(false);
            DrainQueueAsCanceled();
            return;
        }

        _threadReady.TrySetResult(true);

        try
        {
            MessageLoopThread();
        }
        finally
        {
            NativeMethods.OleUninitialize();
        }
    }

    private void MessageLoopThread()
    {
        // Suppressed: SafeWaitHandle.DangerousGetHandle is required here because we
        // pass the handles to a Win32 API (MsgWaitForMultipleObjects) that accepts
        // native HANDLEs. The handles are kept alive by the AutoResetEvent/ManualResetEvent
        // fields for the lifetime of the scheduler, so there is no reference-count race.
#pragma warning disable S3869, CC0001
        IntPtr[] handles = [_workAvailable.SafeWaitHandle.DangerousGetHandle(), _shutdownEvent.SafeWaitHandle.DangerousGetHandle()];
#pragma warning restore CC0001, S3869

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

        DrainQueueAsCanceled();
    }

    private void ProcessQueuedItems()
    {
        while (_queue.TryDequeue(out var item))
        {
            item.Execute();
            NativeMethods.PumpPendingMessages();

            if (_isShuttingDown)
            {
                return;
            }
        }
    }

    private void DrainQueueAsCanceled()
    {
        while (_queue.TryDequeue(out var item))
        {
            item.Cancel();
        }
    }
}
