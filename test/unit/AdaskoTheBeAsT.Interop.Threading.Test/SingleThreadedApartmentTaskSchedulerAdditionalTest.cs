using System;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Xunit;
#if NET8_0_OR_GREATER
using System.Runtime.Versioning;
#endif

namespace AdaskoTheBeAsT.Interop.Threading.Test;

#if NET8_0_OR_GREATER
[SupportedOSPlatform("windows")]
#endif
public class SingleThreadedApartmentTaskSchedulerAdditionalTest
{
    [Fact]
    public void Ctor_NegativeDefaultTimeout_Throws()
    {
        SkipIfNotWindows();

        // Note: TimeSpan.FromMilliseconds(-1) equals Timeout.InfiniteTimeSpan
        // in .NET, so we use -5ms to ensure the value is strictly negative
        // and NOT the infinite sentinel.
        var options = new SingleThreadedApartmentTaskSchedulerOptions
        {
            DefaultWorkItemTimeout = TimeSpan.FromMilliseconds(-5),
        };

        var act = () => new SingleThreadedApartmentTaskScheduler(options);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .And.ParamName.Should().Be(nameof(options));
    }

    [Fact]
    public async Task Ctor_NullOptions_UsesDefaultsAsync()
    {
        SkipIfNotWindows();

        using var scheduler = new SingleThreadedApartmentTaskScheduler(options: null);

        // Observable default: the STA thread name equals
        // SingleThreadedApartmentTaskSchedulerOptions.DefaultThreadName.
        var threadName = await scheduler.RunAsync(
            static _ => Thread.CurrentThread.Name,
            CancellationToken.None);

        threadName.Should().Be(SingleThreadedApartmentTaskSchedulerOptions.DefaultThreadName);
    }

    [Fact]
    public async Task Ctor_WhitespaceThreadName_FallsBackToDefaultAsync()
    {
        SkipIfNotWindows();

        using var scheduler = new SingleThreadedApartmentTaskScheduler(
            new SingleThreadedApartmentTaskSchedulerOptions { ThreadName = "   " });

        // Observable fallback: whitespace ThreadName is replaced with DefaultThreadName.
        var threadName = await scheduler.RunAsync(
            static _ => Thread.CurrentThread.Name,
            CancellationToken.None);

        threadName.Should().Be(SingleThreadedApartmentTaskSchedulerOptions.DefaultThreadName);
    }

    [Fact]
    public async Task RunAsync_StaYieldGeneric_ExecutesAndReturnsValueAsync()
    {
        SkipIfNotWindows();

        using var scheduler = new SingleThreadedApartmentTaskScheduler();

        var result = await scheduler.RunAsync<int>(
            static _ =>
            {
                Thread.CurrentThread.GetApartmentState().Should().Be(ApartmentState.STA);
                return 42;
            },
            CancellationToken.None);

        result.Should().Be(42);
    }

    [Fact]
    public async Task RunAsync_StaYieldAction_CompletesAsync()
    {
        SkipIfNotWindows();

        using var scheduler = new SingleThreadedApartmentTaskScheduler();

        var observed = 0;
        await scheduler.RunAsync(
            yield =>
            {
                yield.Should().NotBeNull();
                Thread.CurrentThread.GetApartmentState().Should().Be(ApartmentState.STA);
                observed = 9;
            },
            CancellationToken.None);

        observed.Should().Be(9);
    }

    [Fact]
    public async Task RunAsync_StaYieldGeneric_NullWork_ThrowsAsync()
    {
        SkipIfNotWindows();

        using var scheduler = new SingleThreadedApartmentTaskScheduler();

        var act = async () => await scheduler.RunAsync<int>(
            (Func<StaYield, int>)null!,
            CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task RunAsync_StaYieldAction_NullWork_ThrowsAsync()
    {
        SkipIfNotWindows();

        using var scheduler = new SingleThreadedApartmentTaskScheduler();

        var act = async () => await scheduler.RunAsync(
            (Action<StaYield>)null!,
            CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task RunAsync_Func_NullFunc_ThrowsAsync()
    {
        SkipIfNotWindows();

        using var scheduler = new SingleThreadedApartmentTaskScheduler();

        var act = async () => await scheduler.RunAsync<int>(
            (Func<int>)null!,
            CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task RunAsync_StaYieldGeneric_PreCanceledToken_ReturnsCanceledTaskAsync()
    {
        SkipIfNotWindows();

        using var scheduler = new SingleThreadedApartmentTaskScheduler();
        using var cts = new CancellationTokenSource();
#if NET8_0_OR_GREATER
        await cts.CancelAsync();
#else
        cts.Cancel();
#endif

        var act = async () => await scheduler.RunAsync<int>(
            _ => 1,
            cts.Token);

        await act.Should().ThrowAsync<TaskCanceledException>();
    }

    [Fact]
    public async Task RunAsync_StaYieldAction_PreCanceledToken_ReturnsCanceledTaskAsync()
    {
        SkipIfNotWindows();

        using var scheduler = new SingleThreadedApartmentTaskScheduler();
        using var cts = new CancellationTokenSource();
#if NET8_0_OR_GREATER
        await cts.CancelAsync();
#else
        cts.Cancel();
#endif

        var act = async () => await scheduler.RunAsync(
            _ => { },
            cts.Token);

        await act.Should().ThrowAsync<TaskCanceledException>();
    }

    [Fact]
    public async Task RunAsync_Timeout_NegativeTimeout_ThrowsAsync()
    {
        SkipIfNotWindows();

        using var scheduler = new SingleThreadedApartmentTaskScheduler();

        var act = async () => await scheduler.RunAsync<int>(
            () => 1,
            TimeSpan.FromMilliseconds(-5),
            CancellationToken.None);

        var assertion = await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
        assertion.And.ParamName.Should().Be("timeout");
    }

    [Fact]
    public async Task RunAsync_Timeout_InfiniteTimeSpan_ReturnsResultAsync()
    {
        SkipIfNotWindows();

        using var scheduler = new SingleThreadedApartmentTaskScheduler();

        var result = await scheduler.RunAsync<int>(
            () => 7,
            Timeout.InfiniteTimeSpan,
            CancellationToken.None);

        result.Should().Be(7);
    }

    [Fact]
    public async Task RunAsync_AfterShutdown_ReturnsFaultedTaskAsync()
    {
        SkipIfNotWindows();

        using var scheduler = new SingleThreadedApartmentTaskScheduler();

        await scheduler.RunAsync<object?>(() => null, CancellationToken.None);

        scheduler.Shutdown();

        var act = async () => await scheduler.RunAsync(() => 1, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*shutting down*");
    }

    [Fact]
    public void Shutdown_IsIdempotent()
    {
        SkipIfNotWindows();

        using var scheduler = new SingleThreadedApartmentTaskScheduler();

        scheduler.Shutdown();
        scheduler.Shutdown();
        scheduler.Shutdown();

        true.Should().BeTrue();
    }

    // Disabled analyzers for this test: IDISP016 and IDISP017.
    // Rationale: the test's exact purpose is to verify that Shutdown is a
    // safe no-op when invoked AFTER Dispose has already torn down the
    // synchronization primitives on the STA thread. A using block would
    // hide the post-dispose call site under test, so an explicit new
    // plus try-finally plus explicit post-dispose call is the minimal
    // faithful reproducer.
#pragma warning disable IDISP016, IDISP017
    [Fact]
    public async Task Shutdown_AfterDispose_DoesNotThrowAsync()
    {
        SkipIfNotWindows();

        var scheduler = new SingleThreadedApartmentTaskScheduler();
        try
        {
            await scheduler.RunAsync<object?>(() => null, CancellationToken.None);
        }
        finally
        {
            scheduler.Dispose();
        }

        var act = () => scheduler.Shutdown();
        act.Should().NotThrow();
    }
#pragma warning restore IDISP016, IDISP017

    // Disabled analyzers for this test: CA2000, AsyncFixer04, IDISP016
    // and IDISP017.
    // Rationale: Dispose is intentionally invoked from inside a scheduled
    // STA work item, which is the scenario under test. The outer
    // try-finally provides a deterministic, idempotent second disposal
    // on a non-STA thread so no handle actually leaks. A using block
    // cannot express this shape because the Dispose call we care about
    // must happen on the STA thread, INSIDE a scheduled delegate.
#pragma warning disable CA2000, AsyncFixer04, IDISP016, IDISP017
    [Fact]
    public async Task Dispose_FromInsideWorkItem_DoesNotDeadlockAsync()
    {
        SkipIfNotWindows();

        var scheduler = new SingleThreadedApartmentTaskScheduler();
        try
        {
            await scheduler.RunAsync<object?>(() => null, CancellationToken.None);

            // Dispose from the STA thread itself. Must not deadlock: the
            // join step is skipped in that branch, and the finally block in
            // ThreadEntry disposes the synchronization primitives once the
            // message loop returns. The delegate's linked CTS is tied to
            // _shutdownCts which Dispose cancels, so the resulting task may
            // legitimately surface as canceled OR completed; the property
            // under test is ONLY that the call does not deadlock.
            Func<Task> selfDisposeAct = async () =>
            {
#pragma warning disable VSTHRD003
                await scheduler.RunAsync<object?>(
                    () =>
                    {
                        scheduler.Dispose();
                        return null;
                    },
                    CancellationToken.None);
#pragma warning restore VSTHRD003
            };

            // Accept either a clean completion or a cancellation from the
            // linked CTS that Dispose cancels; anything else (including a
            // TimeoutException / deadlock-surrogate) fails the test.
            var thrown = await Record.ExceptionAsync(selfDisposeAct);
            (thrown is null || thrown is OperationCanceledException)
                .Should()
                .BeTrue("self-dispose from inside a work item should either complete or surface as cancellation, never deadlock");

            // After self-dispose, the scheduler must consistently fault new work.
            var act = async () => await scheduler.RunAsync(() => 1, CancellationToken.None);
            await act.Should().ThrowAsync<ObjectDisposedException>();
        }
        finally
        {
            // Idempotent second disposal on a non-STA thread.
            scheduler.Dispose();
        }
    }
#pragma warning restore CA2000, AsyncFixer04, IDISP016, IDISP017

    [Fact]
    public async Task RunAsync_WithCustomThreadName_UsesItAsync()
    {
        SkipIfNotWindows();

        using var scheduler = new SingleThreadedApartmentTaskScheduler(
            new SingleThreadedApartmentTaskSchedulerOptions { ThreadName = "Custom-STA-Name" });

        var name = await scheduler.RunAsync(
            () => Thread.CurrentThread.Name,
            CancellationToken.None);

        name.Should().Be("Custom-STA-Name");
    }

    [Fact]
    public async Task RunAsync_MultipleTasksQueuedBeforeExecution_AllRunSequentiallyAsync()
    {
        SkipIfNotWindows();

        const int count = 25;
        var executionOrder = new int[count];
        var executionStep = 0;

        using var scheduler = new SingleThreadedApartmentTaskScheduler();
        await scheduler.RunAsync<object?>(() => null, CancellationToken.None);

        var tasks = new Task[count];

        // AsyncFixer04 disabled here because the purpose of this test is to
        // queue multiple items BEFORE awaiting any of them, then await the
        // whole batch via Task.WhenAll below. The using block disposes the
        // scheduler only after Task.WhenAll completes, so no task is ever
        // left hanging past the scheduler's lifetime.
#pragma warning disable AsyncFixer04
        for (var i = 0; i < count; i++)
        {
            var captured = i;
            tasks[i] = scheduler.RunAsync<object?>(
                () =>
                {
                    // Record the actual position in the execution sequence
                    // using an atomic counter so that the assertion below
                    // detects real out-of-order execution rather than
                    // tautologically passing (executionOrder[i] == i is true
                    // regardless of ordering when each item writes its own
                    // captured index to its own slot).
                    var step = Interlocked.Increment(ref executionStep) - 1;
                    executionOrder[step] = captured;
                    return null;
                },
                CancellationToken.None);
        }
#pragma warning restore AsyncFixer04

        await Task.WhenAll(tasks);

        for (var i = 0; i < count; i++)
        {
            executionOrder[i].Should().Be(i);
        }
    }

    [Fact]
    public void Options_Defaults_AreSane()
    {
        var options = new SingleThreadedApartmentTaskSchedulerOptions();

        options.ThreadName.Should().Be(SingleThreadedApartmentTaskSchedulerOptions.DefaultThreadName);
        options.DefaultWorkItemTimeout.Should().Be(Timeout.InfiniteTimeSpan);
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
