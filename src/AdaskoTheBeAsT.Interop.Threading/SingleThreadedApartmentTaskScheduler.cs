using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
#if NET8_0_OR_GREATER
using System.Runtime.Versioning;
#endif
using System.Threading;
using System.Threading.Tasks;

namespace AdaskoTheBeAsT.Interop.Threading;

/// <summary>Serializes synchronous delegates on one owned OLE-initialized STA thread.</summary>
/// <remarks>
/// Timeouts stop waiting, not native execution. Dispose can wait indefinitely for noncooperative work.
/// Pumping permits reentrant callbacks. Never pass async delegates or share StaYield across threads.
/// </remarks>
#if NET8_0_OR_GREATER
[SupportedOSPlatform("windows")]
#endif
public sealed class SingleThreadedApartmentTaskScheduler : ICooperativeStaTaskScheduler
{
    private static int _nextSchedulerId;
    private readonly LinkedList<IStaWorkItem> _queue = new();
#if NET9_0_OR_GREATER
    private readonly Lock _lifecycleGate = new();
#else
    private readonly object _lifecycleGate = new();
#endif
    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "Owned by the worker after Start, constructor otherwise.")]
    [SuppressMessage("IDisposableAnalyzers", "IDISP002:Dispose member", Justification = "The worker owns native handles after Start.")]
    private readonly AutoResetEvent _workAvailable = null!;
    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "Owned by the worker after Start, constructor otherwise.")]
    private readonly ManualResetEvent _shutdownEvent = null!;
    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "Disposed after worker exit and the last shutdown signal lease.")]
    [SuppressMessage("IDisposableAnalyzers", "IDISP002:Dispose member", Justification = "Disposed after worker exit and cancellation lease completion.")]
    private readonly CancellationTokenSource _shutdownCts = null!;
    private readonly Thread _thread;
    private readonly StaPlatform _platform;
    private readonly TimeSpan _defaultTimeout;
    private readonly int? _maximumPending;
    private readonly bool _diagnostics;
    private readonly int _schedulerId = Interlocked.Increment(ref _nextSchedulerId);
    private readonly TaskCompletionSource<bool> _threadReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private volatile bool _isShuttingDown;
    private Exception? _terminalException;
    private bool _workerExited;
    private int _shutdownSignals;
    private int _disposedState;
    private long _nextWorkId;

    /// <summary>Initializes a new instance of the <see cref="SingleThreadedApartmentTaskScheduler"/> class.</summary>
    public SingleThreadedApartmentTaskScheduler()
        : this(options: null)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="SingleThreadedApartmentTaskScheduler"/> class.</summary>
    /// <param name="options">Options, or null for defaults.</param>
    public SingleThreadedApartmentTaskScheduler(SingleThreadedApartmentTaskSchedulerOptions? options)
        : this(options, new StaPlatform(), TimeSpan.FromSeconds(30))
    {
    }

    internal SingleThreadedApartmentTaskScheduler(
        SingleThreadedApartmentTaskSchedulerOptions? options, StaPlatform platform, TimeSpan initializationTimeout)
    {
        var effective = options ?? new SingleThreadedApartmentTaskSchedulerOptions();
        TimeoutValidation.Validate(effective.DefaultWorkItemTimeout, nameof(options));
        TimeoutValidation.Validate(initializationTimeout, nameof(initializationTimeout));
        if (effective.MaximumPendingWorkItems is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "MaximumPendingWorkItems must be positive or null.");
        }

        _defaultTimeout = effective.DefaultWorkItemTimeout;
        _maximumPending = effective.MaximumPendingWorkItems;
        _diagnostics = effective.EnableDiagnostics;
        _platform = platform;
        _thread = new Thread(ThreadEntry)
        {
            IsBackground = true,
            Name = string.IsNullOrWhiteSpace(effective.ThreadName)
                ? SingleThreadedApartmentTaskSchedulerOptions.DefaultThreadName : effective.ThreadName,
        };
        try
        {
            _workAvailable = new AutoResetEvent(initialState: false);
            _shutdownEvent = new ManualResetEvent(initialState: false);
            _shutdownCts = new CancellationTokenSource();
            _platform.Start(_thread);
        }
        catch
        {
            _workAvailable?.Dispose();
            _shutdownEvent?.Dispose();
            _shutdownCts?.Dispose();
            throw;
        }

        TaskWait.ObserveFault(_completion.Task);
        TaskWait.ObserveFault(_threadReady.Task);
        WaitForThreadInitialization(initializationTimeout);
    }

    /// <inheritdoc />
    public Task Completion => _completion.Task;

    /// <summary>Gets the number of pending items, excluding the currently executing item.</summary>
    public int PendingWorkItemCount
    {
        get
        {
            lock (_lifecycleGate)
            {
                return _queue.Count;
            }
        }
    }

    /// <summary>Gets the exit code if the worker consumed WM_QUIT; otherwise null.</summary>
    public int? QuitExitCode { get; private set; }

    // Delegates are validated by ValidateWork before wrapping them.
#pragma warning disable CC0031
    /// <inheritdoc />
    public Task<T?> RunAsync<T>(Func<StaYield, T?> work, CancellationToken cancellationToken = default)
    {
        ValidateWork(work, nameof(work));
        return ScheduleAsync(_ => work(new StaYield()), _defaultTimeout, cancellationToken);
    }

    /// <inheritdoc />
    public Task RunAsync(Action<StaYield> work, CancellationToken cancellationToken = default)
    {
        ValidateWork(work, nameof(work));
        return ScheduleAsync<object?>(
            _ =>
            {
                work(new StaYield());
                return null;
            },
            _defaultTimeout,
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<T?> RunAsync<T>(Func<T?> func, CancellationToken cancellationToken)
    {
        return RunAsync(func, _defaultTimeout, cancellationToken);
    }

    /// <inheritdoc />
    public Task<T?> RunAsync<T>(Func<T?> func, TimeSpan timeout, CancellationToken cancellationToken)
    {
        ValidateWork(func, nameof(func));
        return ScheduleAsync(_ => func(), timeout, cancellationToken);
    }

    /// <summary>Schedules synchronous cooperative work using the default timeout.</summary>
    /// <typeparam name="T">Result type.</typeparam>
    /// <param name="work">Synchronous delegate receiving the effective cancellation token.</param>
    /// <param name="cancellationToken">Caller cancellation.</param>
    /// <returns>The operation's outcome.</returns>
    public Task<T?> RunCooperativeAsync<T>(Func<StaYield, CancellationToken, T?> work, CancellationToken cancellationToken = default)
    {
        return RunCooperativeAsync(work, _defaultTimeout, cancellationToken);
    }

    /// <inheritdoc />
    public Task<T?> RunCooperativeAsync<T>(
        Func<StaYield, CancellationToken, T?> work, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        ValidateWork(work, nameof(work));
        return ScheduleAsync(token => work(new StaYield(), token), timeout, cancellationToken);
    }

    /// <summary>Schedules a synchronous cooperative action using the default timeout.</summary>
    /// <param name="work">Synchronous action, never async void.</param>
    /// <param name="cancellationToken">Caller cancellation.</param>
    /// <returns>The operation's outcome.</returns>
    public Task RunCooperativeAsync(Action<StaYield, CancellationToken> work, CancellationToken cancellationToken = default)
    {
        return RunCooperativeAsync(work, _defaultTimeout, cancellationToken);
    }

    /// <inheritdoc />
    public Task RunCooperativeAsync(
        Action<StaYield, CancellationToken> work, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        ValidateWork(work, nameof(work));
        return ScheduleAsync<object?>(
            token =>
            {
                work(new StaYield(), token);
                return null;
            },
            timeout,
            cancellationToken);
    }

#pragma warning restore CC0031

    /// <inheritdoc />
    public void Shutdown()
    {
        lock (_lifecycleGate)
        {
            if (_isShuttingDown)
            {
                return;
            }

            _isShuttingDown = true;
            _shutdownSignals++;
            _shutdownEvent.Set();
        }

        // User cancellation callbacks must never run under the admission lock.
        // A lease prevents worker teardown from disposing this source during Cancel.
        _ = Task.Run(SignalShutdown, CancellationToken.None);
    }

    /// <inheritdoc />
    public Task ShutdownAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        TimeoutValidation.Validate(timeout, nameof(timeout));
        Shutdown();
#pragma warning disable VSTHRD003 // Explicitly waiting for the owned worker is this API's purpose.
        return Completion.TimeoutAfterAsync(timeout, cancellationToken);
#pragma warning restore VSTHRD003
    }

    /// <summary>
    /// Requests shutdown and joins the worker. This may wait indefinitely for a blocked delegate.
    /// Self-disposal skips the join; every later external disposal still waits for actual thread exit.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _disposedState, 1, 0) == 0)
        {
            Shutdown();
        }

        if (_thread != Thread.CurrentThread)
        {
            _thread.Join();
        }
    }

    private static void ValidateWork(Delegate work, string parameterName)
    {
#if NET8_0_OR_GREATER
#pragma warning disable S3236 // Preserve the public parameter name through this shared guard.
        ArgumentNullException.ThrowIfNull(work, parameterName);
#pragma warning restore S3236
#else
        if (work is null)
        {
            throw new ArgumentNullException(parameterName);
        }
#endif
    }

    private static TimeSpan Remaining(TimeSpan timeout, Stopwatch elapsed)
    {
        var remaining = timeout - elapsed.Elapsed;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    private static void CancelSafely(CancellationTokenSource source)
    {
        try
        {
            source.Cancel();
        }
        catch (AggregateException)
        {
            // Cancellation callbacks belong to callers; they cannot kill the worker.
            Debug.WriteLine("A cooperative cancellation callback failed.");
        }
    }

    // A constructor must synchronize readiness before admitting calls.
#pragma warning disable VSTHRD002, MA0040
    private void WaitForThreadInitialization(TimeSpan timeout)
    {
        if (Task.WaitAny([_threadReady.Task], timeout) < 0)
        {
            Shutdown();
            throw new InvalidOperationException($"Timed out after {timeout} waiting for the STA worker to initialize.");
        }

        _threadReady.Task.GetAwaiter().GetResult();
    }
#pragma warning restore VSTHRD002, MA0040

    private Task<T?> ScheduleAsync<T>(Func<CancellationToken, T?> work, TimeSpan timeout, CancellationToken token)
    {
        TimeoutValidation.Validate(timeout, nameof(timeout));
        if (token.IsCancellationRequested)
        {
            return Task.FromCanceled<T?>(token);
        }

        lock (_lifecycleGate)
        {
            if (_terminalException is not null)
            {
                return Task.FromException<T?>(_terminalException);
            }

            if (_disposedState != 0)
            {
                return Task.FromException<T?>(new ObjectDisposedException(nameof(SingleThreadedApartmentTaskScheduler)));
            }

            if (_isShuttingDown)
            {
                return Task.FromException<T?>(new InvalidOperationException("The STA task scheduler is shutting down."));
            }

            if (_maximumPending.HasValue && _queue.Count >= _maximumPending.Value)
            {
                TraceWork(0, "rejected", 0);
                return Task.FromException<T?>(new InvalidOperationException("The STA scheduler pending queue is full."));
            }

            return AdmitAsync(work, timeout, token);
        }
    }

    // Called with the gate held. The budget starts here, not after dequeue.
#pragma warning disable CA2000 // Ownership transfers to FinishWaitAsync.
    private Task<T?> AdmitAsync<T>(Func<CancellationToken, T?> work, TimeSpan timeout, CancellationToken callerToken)
    {
        var admitted = Stopwatch.StartNew();
        var id = ++_nextWorkId;
        var timeoutCts = new CancellationTokenSource();
        var linked = CancellationTokenSource.CreateLinkedTokenSource(callerToken, _shutdownCts.Token, timeoutCts.Token);
        var node = new LinkedListNode<IStaWorkItem>(null!);
        var item = new StaWorkItem<T>(
            () => ExecuteWork(work, id, admitted, linked.Token),
            linked.Token,
            () => RemoveCanceled(node, id));
        node.Value = item;
        if (!item.Task.IsCompleted)
        {
            _queue.AddLast(node);
            _workAvailable.Set();
        }

        TraceWork(id, "queued", 0);
        var wait = timeout == Timeout.InfiniteTimeSpan
            ? item.Task
            : TaskWait.StartAsync(
                item.Task,
                Remaining(timeout, admitted),
                static source => ((Task<T?>)source).GetAwaiter().GetResult(),
                item.CancelPending,
                callerToken);
        StartFinishWait(item, wait, timeoutCts, linked, id, admitted);
        return wait;
    }
#pragma warning restore CA2000

    private void StartFinishWait<T>(
        StaWorkItem<T> item,
        Task<T?> wait,
        CancellationTokenSource timeoutCts,
        CancellationTokenSource linked,
        long id,
        Stopwatch admitted)
    {
        // Separate closure scope: the monitor must not retain the original delegate.
#pragma warning disable VSTHRD003
        _ = Task.Run(() => FinishWaitAsync(item, wait, timeoutCts, linked, id, admitted), CancellationToken.None);
#pragma warning restore VSTHRD003
    }

    private T? ExecuteWork<T>(Func<CancellationToken, T?> work, long id, Stopwatch admitted, CancellationToken token)
    {
        TraceWork(id, "running", admitted.Elapsed.TotalMilliseconds);
        var execution = Stopwatch.StartNew();
        try
        {
#pragma warning disable CC0031 // Validated at the public scheduling boundary.
            return work(token);
#pragma warning restore CC0031
        }
        finally
        {
            TraceWork(id, "execution-ended", execution.Elapsed.TotalMilliseconds);
        }
    }

    private async Task FinishWaitAsync<T>(
        StaWorkItem<T> item,
        Task<T?> wait,
        CancellationTokenSource timeoutCts,
        CancellationTokenSource linked,
        long id,
        Stopwatch admitted)
    {
        // WhenAny only waits for terminal state, preserving faulted OCE classification.
#pragma warning disable VSTHRD003
        await Task.WhenAny(wait).ConfigureAwait(false);
#pragma warning restore VSTHRD003
        if (wait != item.Task && (wait.IsCanceled || wait.Exception?.InnerException is TimeoutException))
        {
            TraceWork(id, wait.IsCanceled ? "wait-canceled" : "timed-out", admitted.Elapsed.TotalMilliseconds);
            TaskWait.ObserveFault(item.Task);
            item.CancelPending();

            // May invoke user callbacks; never delay completion of the caller's wait.
            CancelSafely(timeoutCts);
        }

#pragma warning disable VSTHRD003
        await Task.WhenAny(item.Task).ConfigureAwait(false);
#pragma warning restore VSTHRD003
        var state = item.Task.IsFaulted ? "faulted" : "completed";
        TraceWork(id, item.Task.IsCanceled ? "canceled" : state, admitted.Elapsed.TotalMilliseconds);
        item.ReleaseRegistration();
#pragma warning disable IDISP007 // Ownership of these sources transfers from AdmitAsync.
        linked.Dispose();
        timeoutCts.Dispose();
#pragma warning restore IDISP007
    }

    private void RemoveCanceled(LinkedListNode<IStaWorkItem> node, long id)
    {
        lock (_lifecycleGate)
        {
            if (node.List is not null)
            {
                _queue.Remove(node);
            }
        }

        TraceWork(id, "pending-canceled", 0);
    }

#pragma warning disable ParallelChecker // The terminal exception is guarded by _lifecycleGate in both methods.
    private void SignalShutdown()
    {
        try
        {
            // Retire pending work before invoking any caller callback, which may block.
            Exception? failure;
            lock (_lifecycleGate)
            {
                failure = _terminalException;
            }

            DrainQueue(failure);
            CancelSafely(_shutdownCts);
        }
        finally
        {
            lock (_lifecycleGate)
            {
                _shutdownSignals--;
                if (_workerExited && _shutdownSignals == 0)
                {
                    _shutdownCts.Dispose();
                }
            }
        }
    }

    private void ThreadEntry()
    {
        var initialized = false;
        Exception? failure = null;
        try
        {
            _platform.Initialize();
            initialized = true;
            _threadReady.TrySetResult(true);
            MessageLoopThread();
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        finally
        {
            if (initialized)
            {
                try
                {
                    _platform.Uninitialize();
                }
                catch (Exception ex)
                {
                    failure ??= ex;
                }
            }

            FinishWorker(failure);
        }
    }

    private void FinishWorker(Exception? failure)
    {
        lock (_lifecycleGate)
        {
            _terminalException = failure;
        }

        if (failure is not null)
        {
            DrainQueue(failure);
        }

        Shutdown();
        DrainQueue(failure);
        lock (_lifecycleGate)
        {
            _workerExited = true;
            _workAvailable.Dispose();
            _shutdownEvent.Dispose();
            if (_shutdownSignals == 0)
            {
                _shutdownCts.Dispose();
            }
        }

        if (failure is not null)
        {
            _threadReady.TrySetException(failure);
        }

        // Joining from a short final continuation makes Completion represent
        // actual thread exit, not just entry into its final cleanup block.
        _ = Task.Run(
            () =>
            {
                _thread.Join();
                if (failure is null)
                {
                    _completion.TrySetResult(true);
                }
                else
                {
                    _completion.TrySetException(failure);
                }

                // Listener callbacks may wait for Completion, so publish it first.
                if (_diagnostics)
                {
                    StaSchedulerEventSource.Log.WorkerStopped(_schedulerId, failure?.HResult ?? 0, QuitExitCode ?? 0);
                }
            },
            CancellationToken.None);
    }
#pragma warning restore ParallelChecker

    private void MessageLoopThread()
    {
#pragma warning disable S3869, CC0001
        IntPtr[] handles = [_workAvailable.SafeWaitHandle.DangerousGetHandle(), _shutdownEvent.SafeWaitHandle.DangerousGetHandle()];
#pragma warning restore S3869, CC0001
        while (!_isShuttingDown)
        {
            var outcome = _platform.Pump(preserveQuit: false);
            if (outcome.QuitSeen)
            {
                QuitExitCode = outcome.ExitCode;
                Shutdown();
                break;
            }

            var item = Dequeue();
            if (item is not null)
            {
                item.Execute();
                continue;
            }

            if (_isShuttingDown)
            {
                break;
            }

            var result = _platform.Wait(handles);
            if (result > NativeMethods.WAIT_OBJECT_0 + handles.Length)
            {
                throw new InvalidOperationException($"Unexpected STA wait result: 0x{result:X8}.");
            }
        }
    }

    private IStaWorkItem? Dequeue()
    {
        lock (_lifecycleGate)
        {
            if (_isShuttingDown || _queue.First is null)
            {
                return null;
            }

            var item = _queue.First.Value;
            _queue.RemoveFirst();
            return item;
        }
    }

    private void DrainQueue(Exception? failure)
    {
        while (true)
        {
            IStaWorkItem item;
            lock (_lifecycleGate)
            {
                if (_queue.First is null)
                {
                    return;
                }

                item = _queue.First.Value;
                _queue.RemoveFirst();
            }

            if (failure is null)
            {
                item.Cancel();
            }
            else
            {
                item.Fail(failure);
            }
        }
    }

    private void TraceWork(long id, string state, double elapsed)
    {
        if (_diagnostics)
        {
            var schedulerId = _schedulerId;
            var pending = PendingWorkItemCount;

            // EventListener callbacks are caller code too. Deliver snapshots off the gate/STA.
            _ = Task.Run(
                () => StaSchedulerEventSource.Log.WorkState(schedulerId, id, state, pending, elapsed),
                CancellationToken.None);
        }
    }
}
