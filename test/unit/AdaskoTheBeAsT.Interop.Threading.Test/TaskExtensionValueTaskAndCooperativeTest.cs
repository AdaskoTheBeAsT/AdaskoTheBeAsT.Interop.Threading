using System;
using System.Threading;
using System.Threading.Tasks;
#if NET8_0_OR_GREATER
using System.Threading.Tasks.Sources;
#endif
using AwesomeAssertions;
using Xunit;

namespace AdaskoTheBeAsT.Interop.Threading.Test;

#pragma warning disable VSTHRD003
[Trait("Category", "Portable")]
public class TaskExtensionValueTaskAndCooperativeTest
{
    [Fact]
    public async Task CooperativeTask_NullOperation_ValidatedBeforeTimeoutAsync()
    {
        const Func<CancellationToken, Task> nullNonGeneric = null!;
        var err1 = await Record.ExceptionAsync(
            async () => await TaskExtension.TimeoutAfterAsync(nullNonGeneric, TimeSpan.FromMilliseconds(-5), CancellationToken.None));
        err1.Should().BeOfType<ArgumentNullException>().Which.ParamName.Should().Be("operation");

        const Func<CancellationToken, Task<int>> nullGeneric = null!;
        var err2 = await Record.ExceptionAsync(
            async () => await TaskExtension.TimeoutAfterAsync(nullGeneric, TimeSpan.FromMilliseconds(-5), CancellationToken.None));
        err2.Should().BeOfType<ArgumentNullException>().Which.ParamName.Should().Be("operation");
    }

    [Fact]
    public async Task CooperativeTask_PreCanceledCallerToken_DoesNotInvokeOperationAsync()
    {
        var invoked = false;
        var canceled = new CancellationToken(canceled: true);

        var err1 = await Record.ExceptionAsync(
            async () => await TaskExtension.TimeoutAfterAsync(
                _ =>
                {
                    invoked = true;
                    return Task.CompletedTask;
                },
                TimeSpan.FromSeconds(5),
                canceled));
        err1.Should().BeAssignableTo<OperationCanceledException>().Which.CancellationToken.Should().Be(canceled);

        var err2 = await Record.ExceptionAsync(
            async () => await TaskExtension.TimeoutAfterAsync(
                _ =>
                {
                    invoked = true;
                    return Task.FromResult(1);
                },
                TimeSpan.FromSeconds(5),
                canceled));
        err2.Should().BeAssignableTo<OperationCanceledException>().Which.CancellationToken.Should().Be(canceled);
        invoked.Should().BeFalse();
    }

    [Fact]
    public async Task CooperativeTask_InfiniteTimeout_PassesCallerTokenDirectlyAsync()
    {
        using var cts = new CancellationTokenSource();
        CancellationToken observedNonGeneric = default;
        CancellationToken observedGeneric = default;

        await TaskExtension.TimeoutAfterAsync(
            ct =>
            {
                observedNonGeneric = ct;
                return Task.CompletedTask;
            },
            Timeout.InfiniteTimeSpan,
            cts.Token);

        var value = await TaskExtension.TimeoutAfterAsync(
            ct =>
            {
                observedGeneric = ct;
                return Task.FromResult(123);
            },
            Timeout.InfiniteTimeSpan,
            cts.Token);

        value.Should().Be(123);
        observedNonGeneric.Should().Be(cts.Token);
        observedGeneric.Should().Be(cts.Token);
    }

    [Fact]
    public async Task CooperativeTask_DeadlineExpiry_CancelsInnerToken_AndSurfacesTimeoutExceptionAsync()
    {
        var innerCanceled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var errGeneric = await Record.ExceptionAsync(
            async () => await TaskExtension.TimeoutAfterAsync<int>(
                async ct =>
                {
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(30), ct);
                        return 42;
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        innerCanceled.TrySetResult(true);
                        throw;
                    }
                },
                TimeSpan.FromMilliseconds(60),
                CancellationToken.None));

        errGeneric.Should().BeOfType<TimeoutException>();
        (await innerCanceled.Task.TimeoutAfterAsync(TimeSpan.FromSeconds(5), CancellationToken.None)).Should().BeTrue();

        var errNonGeneric = await Record.ExceptionAsync(
            async () => await TaskExtension.TimeoutAfterAsync(
                static async ct => await Task.Delay(TimeSpan.FromSeconds(30), ct),
                TimeSpan.FromMilliseconds(60),
                CancellationToken.None));

        errNonGeneric.Should().BeOfType<TimeoutException>();

        // Zero-timeout synchronous ThrowIfCancellationRequested inside operation also maps to TimeoutException.
        var syncZeroTimeout = await Record.ExceptionAsync(
            async () => await TaskExtension.TimeoutAfterAsync(
                static ct =>
                {
                    ct.ThrowIfCancellationRequested();
                    return Task.FromResult(7);
                },
                TimeSpan.Zero,
                CancellationToken.None));

        syncZeroTimeout.Should().BeOfType<TimeoutException>();
    }

    [Fact]
    public async Task CooperativeTask_CallerCancellation_SurfacesOperationCanceledWithCallerTokenAsync()
    {
        using var cts = new CancellationTokenSource();
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var pending = TaskExtension.TimeoutAfterAsync<int>(
            async ct =>
            {
                started.TrySetResult(true);
                await Task.Delay(TimeSpan.FromSeconds(30), ct);
                return 9;
            },
            TimeSpan.FromSeconds(30),
            cts.Token);

        await started.Task.TimeoutAfterAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
#if NET8_0_OR_GREATER
        await cts.CancelAsync();
#else
        cts.Cancel();
#endif

        var err = await Record.ExceptionAsync(async () => await pending);
        err.Should().BeAssignableTo<OperationCanceledException>().Which.CancellationToken.Should().Be(cts.Token);
    }

    [Fact]
    public async Task CooperativeTask_PreservesUnrelatedOceFault_AndSuppressesLateAbandonedFaultAsync()
    {
        var unrelated = new OperationCanceledException(new CancellationToken(canceled: true));
        var faultedWait = TaskExtension.TimeoutAfterAsync(
            _ => Task.FromException<int>(unrelated),
            TimeSpan.FromSeconds(5),
            CancellationToken.None);
        (await Record.ExceptionAsync(async () => await faultedWait)).Should().BeSameAs(unrelated);
        faultedWait.IsFaulted.Should().BeTrue();

        Exception? unobserved = null;
        EventHandler<UnobservedTaskExceptionEventArgs> handler = (_, args) =>
        {
            unobserved = args.Exception;
            args.SetObserved();
        };
        TaskScheduler.UnobservedTaskException += handler;
        try
        {
            await TriggerAbandonedCooperativeFaultAsync();
#pragma warning disable S1215 // Explicit GC cycle needed to test UnobservedTaskException finalization.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
#pragma warning restore S1215
            unobserved.Should().BeNull();
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= handler;
        }
    }

#if NET8_0_OR_GREATER
    [Fact]
    public async Task ValueTask_CompletedFastPath_AllocatesZeroBytes_AndConsumesSourceOnceAsync()
    {
        var genericSource = new SingleUseValueTaskSource<int>();
        var voidSource = new SingleUseVoidValueTaskSource();

        // Warm up JIT for both generic and non-generic ValueTask fast paths.
        _ = await new ValueTask<int>(1).TimeoutAfterAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
        await ValueTask.CompletedTask.TimeoutAfterAsync(TimeSpan.FromSeconds(5), CancellationToken.None);

        var beforeBytes = GC.GetAllocatedBytesForCurrentThread();
        var fastResult = await new ValueTask<int>(42).TimeoutAfterAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
        await ValueTask.CompletedTask.TimeoutAfterAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;

        fastResult.Should().Be(42);
        allocated.Should().Be(0);

        genericSource.ResetAndCompleteSynchronously(314);
        var pooledResult = await genericSource.CreateValueTaskAsync().TimeoutAfterAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
        pooledResult.Should().Be(314);
        genericSource.GetResultCallCount.Should().Be(1);

        voidSource.ResetAndCompleteSynchronously();
        await voidSource.CreateValueTaskAsync().TimeoutAfterAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
        voidSource.GetResultCallCount.Should().Be(1);
    }

    [Fact]
    public async Task ValueTask_PooledSource_ConsumedStrictlyOnceAcrossAsyncTimeoutCancelAndLateFaultAsync()
    {
        // 1. Asynchronous completion within budget (generic + void)
        var asyncSource = new SingleUseValueTaskSource<int>();
        var asyncWait = asyncSource.CreateValueTaskAsync().TimeoutAfterAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
        asyncSource.SetResult(777);
        (await asyncWait).Should().Be(777);
        asyncSource.GetResultCallCount.Should().Be(1);

        var asyncVoidSource = new SingleUseVoidValueTaskSource();
        var asyncVoidWait = asyncVoidSource.CreateValueTaskAsync().TimeoutAfterAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
        asyncVoidSource.SetResult();
        await asyncVoidWait;
        asyncVoidSource.GetResultCallCount.Should().Be(1);

        // 2. Timeout abandonment followed by late fault
        var timeoutSource = new SingleUseValueTaskSource<int>();
        var timeoutErr = await Record.ExceptionAsync(
            async () => await timeoutSource.CreateValueTaskAsync().TimeoutAfterAsync(TimeSpan.FromMilliseconds(50), CancellationToken.None));
        timeoutErr.Should().BeOfType<TimeoutException>();
        timeoutSource.SetException(new InvalidOperationException("late-pooled-fault"));
        timeoutSource.GetResultCallCount.Should().Be(1);

        // 3. Caller cancellation followed by late completion
        using var cts = new CancellationTokenSource();
        var cancelSource = new SingleUseVoidValueTaskSource();
        var cancelWait = cancelSource.CreateValueTaskAsync().TimeoutAfterAsync(TimeSpan.FromSeconds(30), cts.Token);
        await cts.CancelAsync();
        var cancelErr = await Record.ExceptionAsync(async () => await cancelWait);
        cancelErr.Should().BeAssignableTo<OperationCanceledException>().Which.CancellationToken.Should().Be(cts.Token);
        cancelSource.SetResult();
        cancelSource.GetResultCallCount.Should().Be(1);

        // 4. Invalid timeout validates and still observes/consumes source once
        var invalidTimeoutSource = new SingleUseValueTaskSource<int>();
        invalidTimeoutSource.ResetAndCompleteSynchronously(5);
        var invalidErr = await Record.ExceptionAsync(
            async () => await invalidTimeoutSource.CreateValueTaskAsync().TimeoutAfterAsync(TimeSpan.FromMilliseconds(-3), CancellationToken.None));
        invalidErr.Should().BeOfType<ArgumentOutOfRangeException>();
        invalidTimeoutSource.GetResultCallCount.Should().Be(1);
    }

    [Fact]
    public async Task CooperativeValueTask_DistinguishesTimeoutCallerCancelAndFastSuccessAsync()
    {
        var source = new SingleUseValueTaskSource<int>();
        source.ResetAndCompleteSynchronously(88);
        var syncVal = await TaskExtension.TimeoutAfterAsync(
            _ => source.CreateValueTaskAsync(),
            TimeSpan.FromSeconds(5),
            CancellationToken.None);
        syncVal.Should().Be(88);
        source.GetResultCallCount.Should().Be(1);

        var voidSource = new SingleUseVoidValueTaskSource();
        voidSource.ResetAndCompleteSynchronously();
        await TaskExtension.TimeoutAfterAsync(
            _ => voidSource.CreateValueTaskAsync(),
            Timeout.InfiniteTimeSpan,
            CancellationToken.None);
        voidSource.GetResultCallCount.Should().Be(1);

        var timeoutErr = await Record.ExceptionAsync(
            async () => await TaskExtension.TimeoutAfterAsync(
                new Func<CancellationToken, ValueTask<int>>(static async ct =>
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), ct);
                    return 1;
                }),
                TimeSpan.FromMilliseconds(50),
                CancellationToken.None));
        timeoutErr.Should().BeOfType<TimeoutException>();

        var voidTimeoutErr = await Record.ExceptionAsync(
            async () => await TaskExtension.TimeoutAfterAsync(
                static ct => new ValueTask(Task.Delay(TimeSpan.FromSeconds(30), ct)),
                TimeSpan.FromMilliseconds(50),
                CancellationToken.None));
        voidTimeoutErr.Should().BeOfType<TimeoutException>();

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var invoked = false;
        var preCanceledErr = await Record.ExceptionAsync(
            async () => await TaskExtension.TimeoutAfterAsync(
                _ =>
                {
                    invoked = true;
                    return new ValueTask<int>(1);
                },
                TimeSpan.FromSeconds(5),
                cts.Token));
        preCanceledErr.Should().BeAssignableTo<OperationCanceledException>().Which.CancellationToken.Should().Be(cts.Token);
        invoked.Should().BeFalse();
    }
#endif

    private static async Task TriggerAbandonedCooperativeFaultAsync()
    {
        var lateFaultSource = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken capturedDeadline = default;
        var err = await Record.ExceptionAsync(
            async () => await TaskExtension.TimeoutAfterAsync(
                ct =>
                {
                    capturedDeadline = ct;
                    return lateFaultSource.Task;
                },
                TimeSpan.FromMilliseconds(40),
                CancellationToken.None));

        err.Should().BeOfType<TimeoutException>();

        // Linked deadline CTS must still allow inspecting its token / WaitHandle before lateFaultSource settles!
        capturedDeadline.IsCancellationRequested.Should().BeTrue();
        _ = capturedDeadline.WaitHandle;

        lateFaultSource.SetException(new InvalidOperationException("abandoned-late-fault"));
        await Task.Yield();
    }

#if NET8_0_OR_GREATER
    private sealed class SingleUseValueTaskSource<T> : IValueTaskSource<T>
    {
#pragma warning disable S3459 // Mutable struct mutated via ref-this methods.
        private ManualResetValueTaskSourceCore<T> _core;
#pragma warning restore S3459
        private int _getResultCalls;
        private bool _consumed;

        public int GetResultCallCount => Volatile.Read(ref _getResultCalls);

        public void ResetAndCompleteSynchronously(T value)
        {
            _core.Reset();
            _getResultCalls = 0;
            _consumed = false;
            _core.SetResult(value);
        }

        public ValueTask<T> CreateValueTaskAsync()
        {
            return new ValueTask<T>(this, _core.Version);
        }

        public void SetResult(T value)
        {
            _core.SetResult(value);
        }

        public void SetException(Exception exception)
        {
            _core.SetException(exception);
        }

        public T GetResult(short token)
        {
            if (Interlocked.Increment(ref _getResultCalls) != 1)
            {
                throw new InvalidOperationException("Pooled IValueTaskSource<T>.GetResult was invoked more than once.");
            }

            var result = _core.GetResult(token);
            _consumed = true;
            _core.Reset();
            return result;
        }

        public ValueTaskSourceStatus GetStatus(short token)
        {
            if (_consumed)
            {
                throw new InvalidOperationException("Pooled IValueTaskSource<T>.GetStatus was touched after consumption.");
            }

            return _core.GetStatus(token);
        }

        public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
        {
            if (_consumed)
            {
                throw new InvalidOperationException("Pooled IValueTaskSource<T>.OnCompleted was touched after consumption.");
            }

            _core.OnCompleted(continuation, state, token, flags);
        }
    }

    private sealed class SingleUseVoidValueTaskSource : IValueTaskSource
    {
#pragma warning disable S3459 // Mutable struct mutated via ref-this methods.
        private ManualResetValueTaskSourceCore<bool> _core;
#pragma warning restore S3459
        private int _getResultCalls;
        private bool _consumed;

        public int GetResultCallCount => Volatile.Read(ref _getResultCalls);

        public void ResetAndCompleteSynchronously()
        {
            _core.Reset();
            _getResultCalls = 0;
            _consumed = false;
            _core.SetResult(result: true);
        }

        public ValueTask CreateValueTaskAsync()
        {
            return new ValueTask(this, _core.Version);
        }

        public void SetResult()
        {
            _core.SetResult(result: true);
        }

        public void GetResult(short token)
        {
            if (Interlocked.Increment(ref _getResultCalls) != 1)
            {
                throw new InvalidOperationException("Pooled IValueTaskSource.GetResult was invoked more than once.");
            }

            _ = _core.GetResult(token);
            _consumed = true;
            _core.Reset();
        }

        public ValueTaskSourceStatus GetStatus(short token)
        {
            if (_consumed)
            {
                throw new InvalidOperationException("Pooled IValueTaskSource.GetStatus was touched after consumption.");
            }

            return _core.GetStatus(token);
        }

        public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
        {
            if (_consumed)
            {
                throw new InvalidOperationException("Pooled IValueTaskSource.OnCompleted was touched after consumption.");
            }

            _core.OnCompleted(continuation, state, token, flags);
        }
    }
#endif
}
