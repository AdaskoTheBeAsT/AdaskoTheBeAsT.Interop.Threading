using System;
using System.Diagnostics;
#if NET8_0_OR_GREATER
using System.Runtime.Versioning;
#endif
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Xunit;

namespace AdaskoTheBeAsT.Interop.Threading.Test;

#if NET8_0_OR_GREATER
[SupportedOSPlatform("windows")]
#endif
public class SingleThreadedApartmentTaskSchedulerCoverageTest
{
    [Fact]
    public async Task RunAsync_Func_WithDefaultTimeoutInfinite_DelegatesToInfiniteBranchAsync()
    {
        SkipIfNotWindows();

        using var scheduler = new SingleThreadedApartmentTaskScheduler();

        // Exercises the RunAsync<T>(Func<T?>, CancellationToken) convenience
        // overload which forwards to the (func, _defaultTimeout, ct) path with
        // the default infinite timeout.
        var result = await scheduler.RunAsync<int>(() => 123, CancellationToken.None);

        result.Should().Be(123);
    }

    [Fact]
    public async Task RunAsync_Func_PreCanceledToken_ReturnsCanceledTaskAsync()
    {
        SkipIfNotWindows();

        using var scheduler = new SingleThreadedApartmentTaskScheduler();
        using var cts = new CancellationTokenSource();
#if NET8_0_OR_GREATER
        await cts.CancelAsync();
#else
        cts.Cancel();
#endif

        // Pre-canceled token must short-circuit before the work item is ever
        // enqueued on the STA thread (exercises the early Task.FromCanceled
        // branch inside RunAsync<T>(Func<T?>, TimeSpan, CancellationToken)).
        var act = async () => await scheduler.RunAsync<int>(
            () => 1,
            Timeout.InfiniteTimeSpan,
            cts.Token);

        await act.Should().ThrowAsync<TaskCanceledException>();
    }

    [Fact]
    public async Task RunAsync_WithFiniteTimeout_CompletesBeforeTimeoutAsync()
    {
        SkipIfNotWindows();

        using var scheduler = new SingleThreadedApartmentTaskScheduler();

        // Exercises the finite-timeout branch where the work item finishes
        // BEFORE the timeout fires, so ObserveTimeoutOutcomeAsync cancels
        // the pending Task.Delay and returns the real result.
        var sw = Stopwatch.StartNew();
        var result = await scheduler.RunAsync<int>(
            () => 42,
            TimeSpan.FromSeconds(5),
            CancellationToken.None);
        sw.Stop();

        result.Should().Be(42);

        // If the timeout had been left running for the full window we would be
        // close to the 5s budget here. A generous 2s upper bound still catches
        // a regression while tolerating CI jitter.
        sw.ElapsedMilliseconds.Should().BeLessThan(2000);
    }

    [Fact]
    public async Task RunAsync_WithFiniteTimeout_TimesOut_ThrowsTimeoutExceptionAsync()
    {
        SkipIfNotWindows();

        using var scheduler = new SingleThreadedApartmentTaskScheduler();

        // Exercises the cooperative timeout path: the delegate ignores its
        // cancellation token and never finishes within the budget, so the
        // Task.Delay wins the WhenAny race and ObserveTimeoutOutcomeAsync
        // throws TimeoutException at the caller's await.
        var act = async () => await scheduler.RunAsync<int>(
            () =>
            {
                // S2925: Thread.Sleep is intentional here - the whole point of
                // this test is a delegate that blocks the STA thread past the
                // timeout budget to force the Task.Delay to win WhenAny.
#pragma warning disable S2925
                Thread.Sleep(500);
#pragma warning restore S2925
                return 1;
            },
            TimeSpan.FromMilliseconds(50),
            CancellationToken.None);

        await act.Should().ThrowAsync<TimeoutException>();
    }

    // Split out to keep each method under MA0051's 60-line ceiling.
    [Fact]
    public async Task RunAsync_WithFiniteTimeout_CallerCancelsFirst_ThrowsOperationCanceledAsync()
    {
        SkipIfNotWindows();

        using var scheduler = new SingleThreadedApartmentTaskScheduler();
        using var cts = new CancellationTokenSource();
        using var started = new ManualResetEventSlim(initialState: false);

        var task = EnqueueCooperativeDelegateAsync(scheduler, cts, started);

        // xUnit1051 + MA0040: ManualResetEventSlim.Wait has an overload that
        // accepts a CancellationToken but the test specifically does NOT want
        // the test-framework's token to short-circuit the cooperative wait
        // under investigation here; passing cts.Token would bypass the very
        // signal (cts.Cancel below) that the assertion is trying to observe.
#pragma warning disable xUnit1051, MA0040
        started.Wait(TimeSpan.FromSeconds(2)).Should().BeTrue();
#pragma warning restore xUnit1051, MA0040

#if NET8_0_OR_GREATER
        await cts.CancelAsync();
#else
        cts.Cancel();
#endif

        Func<Task> act = async () =>
        {
#pragma warning disable VSTHRD003
            _ = await task;
#pragma warning restore VSTHRD003
        };
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task RunAsync_Work_ThrowsExceptionAsync()
    {
        SkipIfNotWindows();

        using var scheduler = new SingleThreadedApartmentTaskScheduler();

        // Exercises the StaWorkItem generic exception-catch branch which
        // surfaces the thrown exception as a faulted Task.
        var act = async () => await scheduler.RunAsync<int>(
            () => throw new InvalidOperationException("boom"),
            CancellationToken.None);

        var assertion = await act.Should().ThrowAsync<InvalidOperationException>();
        assertion.And.Message.Should().Be("boom");
    }

    [Fact]
    public async Task RunAsync_Work_ThrowsOce_WithMatchingToken_SurfacesCanceledAsync()
    {
        SkipIfNotWindows();

        using var scheduler = new SingleThreadedApartmentTaskScheduler();
        using var cts = new CancellationTokenSource();

        // The delegate throws OCE tied to the caller's token AFTER the token
        // has been canceled; exercises the StaWorkItem "catch OCE when token
        // matches" branch which maps to TrySetCanceled(token).
        var task = scheduler.RunAsync<int>(
            () =>
            {
                cts.Cancel();
                cts.Token.ThrowIfCancellationRequested();
                return 0;
            },
            cts.Token);

        Func<Task> act = async () =>
        {
#pragma warning disable VSTHRD003
            _ = await task;
#pragma warning restore VSTHRD003
        };
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // IDISP016/IDISP017: an explicit new + Dispose is needed because the test
    // specifically verifies that a SECOND Dispose() call on an already-disposed
    // scheduler is a no-op; a using block would hide the second call.
#pragma warning disable IDISP016, IDISP017
    [Fact]
    public async Task Dispose_Idempotent_DoesNotThrowAsync()
    {
        SkipIfNotWindows();

        var scheduler = new SingleThreadedApartmentTaskScheduler();
        await scheduler.RunAsync<object?>(() => null, CancellationToken.None);

        scheduler.Dispose();
        var act = () => scheduler.Dispose();

        act.Should().NotThrow();
    }

    // IDISP016/IDISP017: the test intentionally calls RunAsync AFTER Dispose
    // to verify the post-dispose fault path; a using block cannot express this.
    [Fact]
    public async Task RunAsync_AfterDispose_ReturnsFaultedTaskAsync()
    {
        SkipIfNotWindows();

        var scheduler = new SingleThreadedApartmentTaskScheduler();
        await scheduler.RunAsync<object?>(() => null, CancellationToken.None);
        scheduler.Dispose();

        // Exercises the _disposedState != 0 early branch in RunAsync which
        // surfaces ObjectDisposedException as a faulted task without
        // touching the now-torn-down synchronization primitives.
        var act = async () => await scheduler.RunAsync<int>(
            () => 1,
            CancellationToken.None);

        await act.Should().ThrowAsync<ObjectDisposedException>();
    }
#pragma warning restore IDISP016, IDISP017

    private static Task<int> EnqueueCooperativeDelegateAsync(
        SingleThreadedApartmentTaskScheduler scheduler,
        CancellationTokenSource cts,
        ManualResetEventSlim started)
    {
        var callerToken = cts.Token;

        // xUnit1051: we intentionally forward the caller's real token into
        // RunAsync because the test is explicitly about cancellation via THAT
        // token. Replacing it with TestContext.Current.CancellationToken would
        // defeat the test.
#pragma warning disable xUnit1051
        return scheduler.RunAsync<int>(
            () =>
            {
                started.Set();

                while (!callerToken.IsCancellationRequested)
                {
                    // S2925: sleep is the cooperative-polling point we want
                    // to exercise; SpinUntil would mask the token observation.
#pragma warning disable S2925
                    Thread.Sleep(10);
#pragma warning restore S2925
                }

                callerToken.ThrowIfCancellationRequested();
                return 1;
            },
            TimeSpan.FromSeconds(5),
            callerToken);
#pragma warning restore xUnit1051
    }

    private static void SkipIfNotWindows()
    {
        if (!IsWindows())
        {
            throw new PlatformNotSupportedException("Scheduler relies on Win32 message pumping; test is Windows-only.");
        }
    }

    private static bool IsWindows()
    {
#if NET5_0_OR_GREATER
        return OperatingSystem.IsWindows();
#else
        return Environment.OSVersion.Platform == PlatformID.Win32NT
               || Environment.OSVersion.Platform == PlatformID.Win32S
               || Environment.OSVersion.Platform == PlatformID.Win32Windows
               || Environment.OSVersion.Platform == PlatformID.WinCE;
#endif
    }
}
