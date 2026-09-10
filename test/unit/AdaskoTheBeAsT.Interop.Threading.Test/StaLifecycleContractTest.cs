using System;
using System.ComponentModel;
#if NET8_0_OR_GREATER
using System.Runtime.Versioning;
#endif
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Xunit;

namespace AdaskoTheBeAsT.Interop.Threading.Test;

// These tests intentionally keep bounded work alive across awaits and join after releasing it.
#pragma warning disable VSTHRD003, AsyncFixer04, IDISP016, IDISP017
#if NET8_0_OR_GREATER
[SupportedOSPlatform("windows")]
#endif
public class StaLifecycleContractTest
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task CooperativeCallerCancellation_PreservesEffectiveTokenAsync()
    {
        using var scheduler = new SingleThreadedApartmentTaskScheduler();
        using var caller = new CancellationTokenSource();
        var started = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var task = scheduler.RunCooperativeAsync(
            (yield, token) =>
            {
                started.TrySetResult(token);
                yield.SpinUntil(static () => false, Budget, token);
                return 1;
            },
            caller.Token);
        var effective = await started.Task.TimeoutAfterAsync(Budget, CancellationToken.None);
#if NET8_0_OR_GREATER
        await caller.CancelAsync();
#else
        caller.Cancel();
#endif
        var error = await Record.ExceptionAsync(async () => await task.TimeoutAfterAsync(Budget, CancellationToken.None));
        task.IsCanceled.Should().BeTrue();
        error.Should().BeAssignableTo<OperationCanceledException>().Which.CancellationToken.Should().Be(effective);
        effective.Should().NotBe(caller.Token);
        (await scheduler.RunAsync(() => 42, CancellationToken.None)).Should().Be(42);
    }

    [Fact]
    public async Task CooperativeTimeout_ReleasesWorkerForNextItemAsync()
    {
        using var scheduler = new SingleThreadedApartmentTaskScheduler();
        var exited = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var task = scheduler.RunCooperativeAsync(
            (yield, token) =>
            {
                try
                {
                    yield.SpinUntil(static () => false, Budget, token);
                    return 0;
                }
                finally
                {
                    exited.TrySetResult(true);
                }
            },
            TimeSpan.FromMilliseconds(200),
            CancellationToken.None);
        var error = await Record.ExceptionAsync(async () => await task);
        error.Should().BeOfType<TimeoutException>();
        task.IsFaulted.Should().BeTrue();
        await exited.Task.TimeoutAfterAsync(Budget, CancellationToken.None);
        (await scheduler.RunAsync(() => 42, CancellationToken.None)).Should().Be(42);
    }

    [Fact]
    public async Task CooperativeShutdown_ExitsAndCompletesWorkerAsync()
    {
        using var scheduler = new SingleThreadedApartmentTaskScheduler();
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var task = scheduler.RunCooperativeAsync(
            (yield, token) =>
            {
                started.TrySetResult(true);
                yield.SpinUntil(static () => false, Budget, token);
            },
            CancellationToken.None);
        await started.Task.TimeoutAfterAsync(Budget, CancellationToken.None);
        await scheduler.ShutdownAsync(Budget, CancellationToken.None);
        task.IsCanceled.Should().BeTrue();
        scheduler.Completion.IsCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task NoncooperativeTimeout_DoesNotReplaceWorker_AndShutdownWaitIsBoundedAsync()
    {
        using var release = new ManualResetEventSlim(initialState: false);
        var started = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var scheduler = new SingleThreadedApartmentTaskScheduler();
        try
        {
            var task = scheduler.RunAsync(
                () =>
                {
                    started.TrySetResult(Environment.CurrentManagedThreadId);
                    release.Wait(Budget, CancellationToken.None);
                    return 1;
                },
                TimeSpan.FromMilliseconds(200),
                CancellationToken.None);
            await started.Task.TimeoutAfterAsync(Budget, CancellationToken.None);
            var nextInvoked = false;
            var next = scheduler.RunAsync(() => nextInvoked = true, CancellationToken.None);
            (await Record.ExceptionAsync(async () => await task)).Should().BeOfType<TimeoutException>();
            nextInvoked.Should().BeFalse();
            (await Record.ExceptionAsync(async () =>
                await scheduler.ShutdownAsync(TimeSpan.FromMilliseconds(50), CancellationToken.None)))
                .Should().BeOfType<TimeoutException>();
            scheduler.Completion.IsCompleted.Should().BeFalse();
            release.Set();
            await scheduler.Completion.TimeoutAfterAsync(Budget, CancellationToken.None);
            next.IsCanceled.Should().BeTrue();
            nextInvoked.Should().BeFalse();
        }
        finally
        {
            release.Set();
            scheduler.Dispose();
        }
    }

    [Fact]
    public async Task CanceledPendingWork_ReleasesCapacityBeforeWorkerReturnsAsync()
    {
        using var release = new ManualResetEventSlim(initialState: false);
        using var cancel = new CancellationTokenSource();
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var scheduler = new SingleThreadedApartmentTaskScheduler(
            new SingleThreadedApartmentTaskSchedulerOptions { MaximumPendingWorkItems = 1 });
        try
        {
            var active = scheduler.RunAsync(
                () =>
                {
                    started.TrySetResult(true);
                    release.Wait(Budget, CancellationToken.None);
                    return 0;
                },
                CancellationToken.None);
            await started.Task.TimeoutAfterAsync(Budget, CancellationToken.None);
            var pending = scheduler.RunAsync(() => 1, cancel.Token);
            var rejected = scheduler.RunAsync(() => 2, CancellationToken.None);
            rejected.IsFaulted.Should().BeTrue();
            _ = rejected.Exception;
#if NET8_0_OR_GREATER
            await cancel.CancelAsync();
#else
            cancel.Cancel();
#endif
            pending.IsCanceled.Should().BeTrue();
            scheduler.PendingWorkItemCount.Should().Be(0);
            var admitted = scheduler.RunAsync(() => 3, CancellationToken.None);
            scheduler.PendingWorkItemCount.Should().Be(1);
            release.Set();
            await active;
            (await admitted).Should().Be(3);
        }
        finally
        {
            release.Set();
        }
    }

    [Fact]
    public async Task LateInitialization_AfterConstructorTimeout_ExitsAsync()
    {
        using var release = new ManualResetEventSlim(initialState: false);
        var platform = new TestStaPlatform { InitializeAction = () => release.Wait(Budget, CancellationToken.None) };
        try
        {
            var act = () =>
            {
                using var scheduler = new SingleThreadedApartmentTaskScheduler(options: null, platform, TimeSpan.FromMilliseconds(50));
            };
            act.Should().Throw<InvalidOperationException>();
        }
        finally
        {
            release.Set();
        }

        (await Task.Run(() => platform.Worker!.Join(Budget), CancellationToken.None)).Should().BeTrue();
        platform.Uninitialized.Should().BeTrue();
    }

    [Fact]
    public void FailedStart_PropagatesOriginalFailure()
    {
        var original = new InvalidOperationException("start failure");
        var platform = new TestStaPlatform { StartFailure = original };
        var act = () =>
        {
            using var scheduler = new SingleThreadedApartmentTaskScheduler(options: null, platform, Budget);
        };
        act.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(original);
        platform.Worker.Should().BeNull();
    }

    [Fact]
    public async Task WaitFailure_CompletesPendingWorkAndRetainsNativeErrorAsync()
    {
        using var failWait = new ManualResetEventSlim(initialState: false);
        var waiting = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var original = new Win32Exception(6);
        var platform = new TestStaPlatform
        {
            WaitAction = () =>
            {
                waiting.TrySetResult(true);
                failWait.Wait(Budget, CancellationToken.None);
                throw original;
            },
        };
        using var scheduler = new SingleThreadedApartmentTaskScheduler(options: null, platform, Budget);
        try
        {
            await waiting.Task.TimeoutAfterAsync(Budget, CancellationToken.None);
            var pending = scheduler.RunAsync(() => 1, CancellationToken.None);
            failWait.Set();
            var error = await Record.ExceptionAsync(async () => await scheduler.Completion.TimeoutAfterAsync(Budget, CancellationToken.None));
            error.Should().BeSameAs(original);
            (await Record.ExceptionAsync(async () => await pending)).Should().BeSameAs(original);
            (await Record.ExceptionAsync(async () => await scheduler.RunAsync(() => 2, CancellationToken.None))).Should().BeSameAs(original);
        }
        finally
        {
            failWait.Set();
        }
    }

    private sealed class TestStaPlatform : StaPlatform
    {
        public Action? InitializeAction { get; set; }

        public Action? WaitAction { get; set; }

        public Exception? StartFailure { get; set; }

        public Thread? Worker { get; private set; }

        public bool Uninitialized { get; private set; }

        public override void Start(Thread thread)
        {
            if (StartFailure is not null)
            {
                throw StartFailure;
            }

            Worker = thread;
            base.Start(thread);
        }

        public override void Initialize()
        {
            InitializeAction?.Invoke();
            base.Initialize();
        }

        public override void Uninitialize()
        {
            base.Uninitialize();
            Uninitialized = true;
        }

        public override uint Wait(IntPtr[] handles)
        {
            WaitAction?.Invoke();
            return base.Wait(handles);
        }
    }
}
