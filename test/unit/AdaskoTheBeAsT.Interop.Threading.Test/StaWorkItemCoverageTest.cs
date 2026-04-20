using System;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Xunit;

namespace AdaskoTheBeAsT.Interop.Threading.Test;

public class StaWorkItemCoverageTest
{
    [Fact]
    public async Task RunAsync_TokenCanceledDuringWork_DelegateIgnores_SurfacesCanceledAsync()
    {
        SkipIfNotWindows();

        using var scheduler = new SingleThreadedApartmentTaskScheduler();
        using var cts = new CancellationTokenSource();
        using var canceledSignal = new ManualResetEventSlim(initialState: false);

        // Delegate that cooperatively waits for the caller's token to cancel,
        // then RETURNS a value WITHOUT throwing OperationCanceledException.
        // StaWorkItem should observe the token has been canceled and surface
        // the task as canceled rather than completed (exercises the branch
        // where _cancellationToken.IsCancellationRequested is true after the
        // delegate returns normally).
        // xUnit1051 + MA0040: the delegate runs on the STA thread and must
        // observe the CALLER's cts.Token via the cooperative cancellation
        // path under test - passing cts.Token into Wait would bypass the
        // exact branch (delegate returns normally, StaWorkItem observes the
        // already-canceled token) that we are trying to exercise.
#pragma warning disable xUnit1051, MA0040
        var task = scheduler.RunAsync<int>(
            () =>
            {
                canceledSignal.Wait(TimeSpan.FromSeconds(5));
                return 42;
            },
            cts.Token);
#pragma warning restore xUnit1051, MA0040

#if NET8_0_OR_GREATER
        await cts.CancelAsync();
#else
        cts.Cancel();
#endif
        canceledSignal.Set();

        Func<Task> act = async () =>
        {
#pragma warning disable VSTHRD003
            _ = await task;
#pragma warning restore VSTHRD003
        };
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // IDISP016/IDISP017: the test intentionally drains the queue via
    // Shutdown and then disposes explicitly in a finally - a using block
    // would dispose BEFORE the drain is observed.
#pragma warning disable IDISP016, IDISP017
    [Fact]
    public async Task Shutdown_WhilePending_CancelsDequeuedWorkItemsAsync()
    {
        SkipIfNotWindows();

        var scheduler = new SingleThreadedApartmentTaskScheduler();
        using var firstRunning = new ManualResetEventSlim(initialState: false);
        using var releaseFirst = new ManualResetEventSlim(initialState: false);

        try
        {
            await RunShutdownDrainScenarioAsync(scheduler, firstRunning, releaseFirst);
        }
        finally
        {
            scheduler.Dispose();
        }
    }
#pragma warning restore IDISP016, IDISP017

    // AsyncFixer01: keeping this async simplifies sequencing (enqueue + await
    // drain) even though the first awaited call is the final statement; the
    // inlined version would obscure the test's intent.
#pragma warning disable AsyncFixer01
    private static async Task RunShutdownDrainScenarioAsync(
        SingleThreadedApartmentTaskScheduler scheduler,
        ManualResetEventSlim firstRunning,
        ManualResetEventSlim releaseFirst)
    {
        // First work item: blocks the STA thread so subsequent items sit in the queue.
        var first = scheduler.RunAsync<object?>(
            () =>
            {
                firstRunning.Set();
                releaseFirst.Wait(TimeSpan.FromSeconds(5));
                return null;
            },
            CancellationToken.None);

        firstRunning.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();

        // AsyncFixer04: queue additional items before awaiting so they sit
        // behind the blocked STA thread; both tasks are awaited below.
#pragma warning disable AsyncFixer04
        var second = scheduler.RunAsync<int>(() => 2, CancellationToken.None);
        var third = scheduler.RunAsync<int>(() => 3, CancellationToken.None);
#pragma warning restore AsyncFixer04

        scheduler.Shutdown();
        releaseFirst.Set();

        await ObserveDrainResultsAsync(first, second, third);
    }
#pragma warning restore AsyncFixer01

    private static async Task ObserveDrainResultsAsync(Task<object?> first, Task<int> second, Task<int> third)
    {
        Func<Task> actSecond = async () =>
        {
#pragma warning disable VSTHRD003
            _ = await second;
#pragma warning restore VSTHRD003
        };
        Func<Task> actThird = async () =>
        {
#pragma warning disable VSTHRD003
            _ = await third;
#pragma warning restore VSTHRD003
        };

        // The first item may complete successfully or be canceled depending on
        // whether shutdown beats the return statement; swallow either outcome
        // via Record.ExceptionAsync which does not require a try/catch.
        _ = await Record.ExceptionAsync(async () =>
        {
#pragma warning disable VSTHRD003
            _ = await first;
#pragma warning restore VSTHRD003
        });

        await actSecond.Should().ThrowAsync<OperationCanceledException>();
        await actThird.Should().ThrowAsync<OperationCanceledException>();
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
