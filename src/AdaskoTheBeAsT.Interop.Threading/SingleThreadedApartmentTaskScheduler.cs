using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
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

    // CA2213 is suppressed because these native-handle-backed primitives are
    // intentionally disposed on the STA thread itself (inside ThreadEntry's
    // finally block) AFTER MsgWaitForMultipleObjects has returned. Disposing
    // them from Dispose() on another thread could close the kernel handles
    // while the STA thread is still blocked on the wait, producing an
    // invalid-handle race. The lifetime owner is the STA thread, not Dispose().
    [SuppressMessage("Microsoft.Usage", "CA2213:Disposable fields should be disposed", Justification = "Disposed on STA thread in ThreadEntry after message loop exits; see ThreadEntry.")]
    private readonly AutoResetEvent _workAvailable = new(initialState: false);

    [SuppressMessage("Microsoft.Usage", "CA2213:Disposable fields should be disposed", Justification = "Disposed on STA thread in ThreadEntry after message loop exits; see ThreadEntry.")]
    private readonly ManualResetEvent _shutdownEvent = new(initialState: false);

    [SuppressMessage("Microsoft.Usage", "CA2213:Disposable fields should be disposed", Justification = "Disposed on STA thread in ThreadEntry after message loop exits; see ThreadEntry.")]
    private readonly CancellationTokenSource _shutdownCts = new();

    private readonly Thread _thread;
    private readonly TimeSpan _defaultTimeout;
    private readonly TaskCompletionSource<bool> _threadReady =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private volatile bool _isShuttingDown;
    private int _disposedState; // 0 = alive, 1 = disposing/disposed. Interlocked.
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

        var defaultTimeout = effectiveOptions.DefaultWorkItemTimeout;
        if (defaultTimeout != Timeout.InfiniteTimeSpan && defaultTimeout < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                defaultTimeout,
                "DefaultWorkItemTimeout must be either Timeout.InfiniteTimeSpan or a non-negative TimeSpan.");
        }

        _defaultTimeout = defaultTimeout;

        _thread = new Thread(ThreadEntry)
        {
            IsBackground = true,
            Name = threadName,
        };

        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();

        // Block briefly until the STA thread has completed OLE initialization (or
        // definitively failed). This guarantees _oleInitResult is observable to the
        // first caller of RunAsync so an initialization failure surfaces as an
        // InvalidOperationException instead of being hidden behind cancellations
        // from DrainQueueAsCanceled().
        //
        // The wait is bounded so that an unexpected ThreadEntry failure before the
        // TrySetResult/TrySetException call (e.g. an AppDomain-unload race, a
        // P/Invoke that hard-faults, or an impossibly slow scheduler pickup of the
        // new thread) surfaces as a clear InvalidOperationException instead of
        // deadlocking construction. ThreadEntry also wraps its body in a try/catch
        // that pushes the exception onto _threadReady, so any throwable propagates
        // here deterministically.
        //
        // VSTHRD002 is suppressed because the blocking wait is intentional and is
        // the synchronization boundary between "ctor returns" and "STA thread is
        // ready to receive work". Making the constructor async would push the
        // waiting burden onto every caller without removing the underlying wait.
#pragma warning disable VSTHRD002
        var initTimeout = TimeSpan.FromSeconds(30);
        if (!_threadReady.Task.Wait(initTimeout))
        {
            throw new InvalidOperationException(
                $"Timed out after {initTimeout} while waiting for the STA thread '{threadName}' to initialize.");
        }

        _threadReady.Task.GetAwaiter().GetResult();
#pragma warning restore VSTHRD002
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

        if (timeout != Timeout.InfiniteTimeSpan && timeout < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeout),
                timeout,
                $"Timeout must be either {nameof(Timeout.InfiniteTimeSpan)} or a non-negative {nameof(TimeSpan)}.");
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<T?>(cancellationToken);
        }

        if (Volatile.Read(ref _disposedState) != 0)
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

        // Shutdown must be idempotent and safe to call after Dispose() (or
        // concurrently with it). Setting a disposed ManualResetEvent throws
        // ObjectDisposedException; swallow it so lifecycle calls are
        // side-effect-free once disposal has already torn the handles down.
        try
        {
            _shutdownEvent.Set();
        }
        catch (ObjectDisposedException ex)
        {
            Debug.WriteLine($"Shutdown event was disposed concurrently: {ex.Message}");
        }
#pragma warning restore CA1031
    }

    /// <summary>
    /// Stops the scheduler and waits for the background STA thread to exit.
    /// The underlying synchronization primitives (<see cref="AutoResetEvent"/>,
    /// <see cref="ManualResetEvent"/>, <see cref="CancellationTokenSource"/>) are
    /// disposed on the STA thread itself after its message loop has returned, so
    /// their native handles are never closed while a kernel wait may still be
    /// blocked on them.
    /// </summary>
    /// <remarks>
    /// When <see cref="Dispose"/> is invoked from the STA thread itself (for example
    /// from inside a work-item delegate that calls <see cref="Dispose"/>), the join
    /// step is skipped to avoid a self-deadlock. The message loop is already
    /// unwinding on that same stack frame, and the final handle disposal still runs
    /// in the <see langword="finally"/> block of <see cref="ThreadEntry"/> once control returns there.
    /// </remarks>
    public void Dispose()
    {
        // Atomic one-winner guard so concurrent Dispose() calls never both run the
        // teardown path (which would race on _thread.Join).
        if (Interlocked.CompareExchange(ref _disposedState, 1, 0) != 0)
        {
            return;
        }

        Shutdown();

        // The STA thread itself performs the final handle/CTS disposal after its
        // message loop exits (see ThreadEntry's finally). We only need to wait for
        // that exit here - unless this call is coming FROM the STA thread, in which
        // case joining would deadlock.
        if (_thread == Thread.CurrentThread)
        {
            return;
        }

#pragma warning disable CA1031
        try
        {
            if (_thread.IsAlive)
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
    }

    private Task<T?> EnqueueAndOptionallyTimeoutAsync<T>(
        Func<T?> func,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        // A Dispose() racing with RunAsync after the _disposedState check can tear
        // down _shutdownCts or _workAvailable while we're mid-enqueue.
        // EnqueueWorkItem surfaces an ObjectDisposedException synchronously; we
        // catch it here and project it into a faulted Task so RunAsync honors its
        // contract of never throwing at the call site on shutdown races.
        if (timeout == Timeout.InfiniteTimeSpan)
        {
            try
            {
                return EnqueueWorkItem(func, cancellationToken);
            }
            catch (ObjectDisposedException ex)
            {
                return Task.FromException<T?>(ex);
            }
        }

        // Cooperative timeout model: the work item itself observes a timeout-aware
        // cancellation token, so when the timeout expires the delegate is asked to
        // cancel and the STA thread is not left blocked running stale work after
        // the caller has already observed a TimeoutException. We then map the
        // resulting cancellation to TimeoutException at the caller's await.
        return EnqueueWithCooperativeTimeoutAsync(func, timeout, cancellationToken);
    }

    // VSTHRD200: this helper is async by design to orchestrate the timeout CTS,
    // so the Async suffix is correct here and RCS1229 is happy.
    private async Task<T?> EnqueueWithCooperativeTimeoutAsync<T>(
        Func<T?> func,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        // CA2000 / IDISP001: ownership of timeoutCts is encapsulated by this
        // method's using statement; it is disposed on every exit path.
#pragma warning disable CA2000, IDISP001
        using var timeoutCts = new CancellationTokenSource();
#pragma warning restore CA2000, IDISP001

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutCts.Token);

        Task<T?> itemTask;
        try
        {
            // Feed the combined caller + timeout token into the work item so the
            // STA delegate cooperatively observes timeout cancellation.
            itemTask = EnqueueWorkItem(func, linkedCts.Token);
        }
        catch (ObjectDisposedException ex)
        {
            return await Task.FromException<T?>(ex).ConfigureAwait(false);
        }

        var delayTask = Task.Delay(timeout, linkedCts.Token);
#pragma warning disable VSTHRD003
        var finished = await Task.WhenAny(itemTask, delayTask).ConfigureAwait(false);
#pragma warning restore VSTHRD003

        return await ObserveTimeoutOutcomeAsync(itemTask, finished, timeout, timeoutCts, cancellationToken).ConfigureAwait(false);
    }

    // Splits the "who finished first" branching off the orchestrator so the
    // outer async method stays under MA0051's 60-line ceiling.
    // AsyncFixer01 is suppressed because, although the "itemTask finished first"
    // branch awaits a single task, the "timeout fired first" branch must throw
    // TimeoutException after honoring caller cancellation, so the method cannot
    // be reduced to "return task directly". The async wrapper is required.
    // SA1204 is suppressed because keeping this static helper grouped with the
    // timeout orchestration it supports is more readable than splitting statics
    // into a separate section at the top of the file.
#pragma warning disable AsyncFixer01, SA1204
    private static async Task<T?> ObserveTimeoutOutcomeAsync<T>(
        Task<T?> itemTask,
        Task finished,
        TimeSpan timeout,
        CancellationTokenSource timeoutCts,
        CancellationToken cancellationToken)
    {
        if (finished == itemTask)
        {
            // The work finished in time. Cancel the pending Task.Delay so it does
            // not keep a timer running for the full timeout window after we have
            // already observed the result.
#if NET8_0_OR_GREATER
            await timeoutCts.CancelAsync().ConfigureAwait(false);
#else
            timeoutCts.Cancel();
#endif

#pragma warning disable VSTHRD003
            return await itemTask.ConfigureAwait(false);
#pragma warning restore VSTHRD003
        }

        // Timeout fired first. Work item now observes the linked token
        // cancellation cooperatively (or is drained on dequeue). Honor caller
        // cancellation first, then surface TimeoutException.
        cancellationToken.ThrowIfCancellationRequested();

        // Avoid UnobservedTaskException when the work item eventually faults;
        // the work item's own ContinueWith (registered in EnqueueWorkItem)
        // still disposes its linked CTS, so there is no resource leak.
        _ = itemTask.ContinueWith(
            static t => _ = t.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        throw new TimeoutException($"The operation has timed out after {timeout}.");
    }
#pragma warning restore AsyncFixer01, SA1204

    // CA2000 / IDISP001: the linked CTS ownership transfers to the ContinueWith
    // continuation, which disposes it when the work item reaches a terminal state.
    // VSTHRD110 / MA0134: the continuation result is intentionally not awaited
    // because its sole purpose is resource cleanup and it never throws.
    // VSTHRD200: this helper is deliberately NOT async and returns the work item's
    // task unwrapped, so the "Async" suffix would be misleading (RCS1229 also
    // forbids the suffix on non-async methods). It only performs a synchronous
    // enqueue and surfaces synchronous ObjectDisposedException to the caller.
#pragma warning disable CA2000, IDISP001, VSTHRD110, MA0134, VSTHRD200, CC0061, RCS1229
    private Task<T?> EnqueueWorkItem<T>(Func<T?> func, CancellationToken cancellationToken)
    {
        CancellationTokenSource? linkedCts = null;
        try
        {
            // Link the caller token with the scheduler's shutdown token so that a
            // pending work item is canceled if the scheduler is disposed before it
            // runs, and a running work item observes cancellation cooperatively.
            linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdownCts.Token);
            var item = new StaWorkItem<T>(func, linkedCts.Token);
            var itemTask = item.Task;

            _queue.Enqueue(item);
            _workAvailable.Set();

            // Clean up the linked CTS once the item completes (in any terminal state).
            _ = itemTask.ContinueWith(
                static (_, state) => ((CancellationTokenSource)state!).Dispose(),
                linkedCts,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

            return itemTask;
        }
        catch (ObjectDisposedException)
        {
            linkedCts?.Dispose();
            throw;
        }
    }
#pragma warning restore CA2000, IDISP001, VSTHRD110, MA0134, VSTHRD200, CC0061, RCS1229

    private void ThreadEntry()
    {
        // CA1031 is suppressed because this is the outermost handler on the STA
        // thread itself. If we let an unexpected exception propagate here, the
        // constructor's bounded Wait on _threadReady would still time out, but
        // the caller would never see the real root cause. Recording the
        // exception on _threadReady.TrySetException makes the exact failure
        // surface synchronously at construction time.
#pragma warning disable CA1031
        try
        {
            try
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
            catch (Exception ex)
            {
                // Ensure the constructor never deadlocks on _threadReady.Task.Wait.
                // TrySetException is a no-op if TrySetResult already ran; that is
                // the intended fallback for pre-init failures only.
                Debug.WriteLine($"STA thread entry faulted before signalling readiness: {ex}");
                _threadReady.TrySetException(ex);
            }
        }
        finally
        {
            DisposeSynchronizationPrimitivesOnStaThread();
        }
#pragma warning restore CA1031
    }

    // Dispose the synchronization primitives on the STA thread itself, AFTER
    // the message loop has returned. This guarantees no kernel wait (inside
    // MsgWaitForMultipleObjects) can still be holding these handles when they
    // are closed, eliminating the invalid-handle race that would occur if
    // Dispose() closed them from another thread while the STA thread was still
    // blocked on the wait.
    // CA1031 is suppressed because Dispose() must never throw - failing to
    // dispose one primitive should not block the others from being cleaned up.
#pragma warning disable CA1031
    private void DisposeSynchronizationPrimitivesOnStaThread()
    {
        try
        {
            _workAvailable.Dispose();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Disposing _workAvailable failed: {ex}");
        }

        try
        {
            _shutdownEvent.Dispose();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Disposing _shutdownEvent failed: {ex}");
        }

        try
        {
            _shutdownCts.Dispose();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Disposing _shutdownCts failed: {ex}");
        }
    }
#pragma warning restore CA1031

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
