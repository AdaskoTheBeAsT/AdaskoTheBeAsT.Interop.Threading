using System;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Xunit;

namespace AdaskoTheBeAsT.Interop.Threading.Test;

public class SingleThreadedApartmentTaskSchedulerExtraCoverageTest
{
    [Fact]
    public async Task RunAsync_AfterShutdown_ButBeforeDispose_ReturnsFaultedTaskAsync()
    {
        SkipIfNotWindows();

        // Exercises the _isShuttingDown early return branch in RunAsync
        // which surfaces InvalidOperationException BEFORE the disposed check
        // would fire.
        using var scheduler = new SingleThreadedApartmentTaskScheduler();
        await scheduler.RunAsync<object?>(() => null, CancellationToken.None);

        scheduler.Shutdown();

        var act = async () => await scheduler.RunAsync<int>(
            () => 1,
            TimeSpan.FromSeconds(1),
            CancellationToken.None);

        var assertion = await act.Should().ThrowAsync<InvalidOperationException>();
        assertion.And.Message.Should().Contain("shutting down");
    }

    [Fact]
    public async Task RunAsync_AfterShutdown_FiniteTimeout_PathAsync()
    {
        SkipIfNotWindows();

        // Same shutdown fault path but through the finite-timeout overload
        // (EnqueueAndOptionallyTimeoutAsync -> EnqueueWithCooperativeTimeoutAsync
        // gate) to cover both routing branches.
        using var scheduler = new SingleThreadedApartmentTaskScheduler();
        await scheduler.RunAsync<object?>(() => null, CancellationToken.None);

        scheduler.Shutdown();

        var act = async () => await scheduler.RunAsync<int>(
            () => 1,
            TimeSpan.FromMilliseconds(100),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task RunAsync_NullWork_FiniteTimeoutOverload_ThrowsAsync()
    {
        SkipIfNotWindows();

        using var scheduler = new SingleThreadedApartmentTaskScheduler();

        var act = async () => await scheduler.RunAsync<int>(
            (Func<int>)null!,
            TimeSpan.FromSeconds(1),
            CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task RunAsync_FuncTimeout_PreCanceledToken_ShortCircuitsAsync()
    {
        SkipIfNotWindows();

        using var scheduler = new SingleThreadedApartmentTaskScheduler();
        using var cts = new CancellationTokenSource();
#if NET8_0_OR_GREATER
        await cts.CancelAsync();
#else
        cts.Cancel();
#endif

        // Exercises the pre-canceled token gate in the Func<T> + timeout
        // overload, which returns Task.FromCanceled without enqueueing.
        var act = async () => await scheduler.RunAsync<int>(
            () => 1,
            TimeSpan.FromSeconds(5),
            cts.Token);

        await act.Should().ThrowAsync<TaskCanceledException>();
    }

    [Fact]
    public async Task RunAsync_LargeTimeout_ThrowsArgumentOutOfRangeAsync()
    {
        SkipIfNotWindows();

        using var scheduler = new SingleThreadedApartmentTaskScheduler();

        // Upper-bound validation branch in RunAsync<T>(Func<T?>, TimeSpan, ct).
        var tooLarge = TimeSpan.FromMilliseconds((double)int.MaxValue + 10.0);

        var act = async () => await scheduler.RunAsync<int>(
            () => 1,
            tooLarge,
            CancellationToken.None);

        var assertion = await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
        assertion.And.ParamName.Should().Be("timeout");
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
