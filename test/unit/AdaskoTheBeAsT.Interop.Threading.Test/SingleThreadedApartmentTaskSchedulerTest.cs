using System;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Xunit;

namespace AdaskoTheBeAsT.Interop.Threading.Test;

public class SingleThreadedApartmentTaskSchedulerTest
{
    [Fact]
    public async Task RunAsync_Generic_ExecutesOnSta_AndReturnsValue()
    {
        SkipIfNotWindows();

        using var scheduler = new SingleThreadedApartmentTaskScheduler();

        var id = await scheduler.RunAsync(
            () =>
            {
                Thread.CurrentThread.GetApartmentState().Should().Be(ApartmentState.STA);
                return Environment.CurrentManagedThreadId;
            },
            CancellationToken.None);

        id.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task RunAsync_Void_CompletesAndPumps()
    {
        SkipIfNotWindows();

        using var scheduler = new SingleThreadedApartmentTaskScheduler();

        var observed = 0;
        await scheduler.RunAsync<object?>(
            () =>
            {
                observed = 7;
                return null;
            },
            CancellationToken.None);

        observed.Should().Be(7);
    }

    [Fact]
    public async Task RunAsync_ProcessesMultipleItems_InOrder()
    {
        SkipIfNotWindows();

        using var scheduler = new SingleThreadedApartmentTaskScheduler();

        var step = 0;
        await scheduler.RunAsync<object?>(
            () =>
            {
                step.Should().Be(0);
                step = 1;
                return null;
            },
            CancellationToken.None);

        await scheduler.RunAsync<object?>(
            () =>
            {
                step.Should().Be(1);
                step = 2;
                return null;
            },
            CancellationToken.None);

        step.Should().Be(2);
    }

    [Fact]
    public async Task RunAsync_PumpsBetweenItems_DoesNotHang()
    {
        SkipIfNotWindows();

        using var scheduler = new SingleThreadedApartmentTaskScheduler();

#pragma warning disable AsyncFixer04
        var first = scheduler.RunAsync<object?>(() => null, CancellationToken.None);
        var second = scheduler.RunAsync<object?>(() => null, CancellationToken.None);
#pragma warning restore AsyncFixer04

        var combined = Task.WhenAll(first, second);
#if NET8_0_OR_GREATER
        var completed = await Task.WhenAny(combined, Task.Delay(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
#else
        var completed = await Task.WhenAny(combined, Task.Delay(TimeSpan.FromSeconds(5)));
#endif
        ReferenceEquals(combined, completed).Should().BeTrue("both items should complete promptly when the pump runs after each item");
    }

    [Fact]
    public async Task RunAsync_PropagatesOriginalException()
    {
        SkipIfNotWindows();

        using var scheduler = new SingleThreadedApartmentTaskScheduler();

        var act = async () =>
            await scheduler.RunAsync<object?>(
                () => throw new InvalidOperationException("boom"),
                CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("boom");
    }

    [Fact]
    public async Task RunAsync_FastFails_WhenAlreadyCanceledAsync()
    {
        SkipIfNotWindows();

        using var scheduler = new SingleThreadedApartmentTaskScheduler();

        using var cts = new CancellationTokenSource();
#if NET8_0_OR_GREATER
        await cts.CancelAsync();
#else
        cts.Cancel();
#endif

        var act = async () => await scheduler.RunAsync(() => 1, cts.Token);

        await act.Should().ThrowAsync<TaskCanceledException>();
    }

#pragma warning disable IDISP016, IDISP017
    [Fact]
    public async Task RunAsync_AfterDispose_Throws()
    {
        SkipIfNotWindows();

        var scheduler = new SingleThreadedApartmentTaskScheduler();
        try
        {
            // Make sure the scheduler is actually running before we dispose it.
            await scheduler.RunAsync<object?>(() => null, CancellationToken.None);
        }
        finally
        {
            scheduler.Dispose();
        }

        var act = async () => await scheduler.RunAsync(() => 1, CancellationToken.None);

        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task Dispose_IsIdempotent()
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
            scheduler.Dispose();
        }

        var act = async () => await scheduler.RunAsync(() => 1, CancellationToken.None);
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }
#pragma warning restore IDISP016, IDISP017

    [Fact]
    public async Task MultipleSchedulers_AreIndependent()
    {
        SkipIfNotWindows();

        using var a = new SingleThreadedApartmentTaskScheduler(
            new SingleThreadedApartmentTaskSchedulerOptions { ThreadName = "STA-A" });
        using var b = new SingleThreadedApartmentTaskScheduler(
            new SingleThreadedApartmentTaskSchedulerOptions { ThreadName = "STA-B" });

        var idA = await a.RunAsync(() => Environment.CurrentManagedThreadId, CancellationToken.None);
        var idB = await b.RunAsync(() => Environment.CurrentManagedThreadId, CancellationToken.None);

        idA.Should().NotBe(idB);
    }

    [Fact]
    public async Task RunAsync_WithTimeout_ThrowsTimeoutExceptionWhenWorkTakesTooLongAsync()
    {
        SkipIfNotWindows();

        using var scheduler = new SingleThreadedApartmentTaskScheduler();

        var act = async () => await scheduler.RunAsync<int>(
            () =>
            {
#pragma warning disable S2925
                Thread.Sleep(1000);
#pragma warning restore S2925
                return 42;
            },
            TimeSpan.FromMilliseconds(50),
            CancellationToken.None);

        await act.Should().ThrowAsync<TimeoutException>();
    }

    [Fact]
    public async Task RunAsync_WithTimeout_ReturnsResultWhenInTime()
    {
        SkipIfNotWindows();

        using var scheduler = new SingleThreadedApartmentTaskScheduler();

        var result = await scheduler.RunAsync<int>(
            () => 123,
            TimeSpan.FromSeconds(5),
            CancellationToken.None);

        result.Should().Be(123);
    }

    [Fact]
    public async Task RunAsync_DefaultTimeoutFromOptions_IsApplied()
    {
        SkipIfNotWindows();

        using var scheduler = new SingleThreadedApartmentTaskScheduler(
            new SingleThreadedApartmentTaskSchedulerOptions
            {
                DefaultWorkItemTimeout = TimeSpan.FromMilliseconds(50),
            });

        var act = async () => await scheduler.RunAsync<int>(
            () =>
            {
#pragma warning disable S2925
                Thread.Sleep(1000);
#pragma warning restore S2925
                return 42;
            },
            CancellationToken.None);

        await act.Should().ThrowAsync<TimeoutException>();
    }

    [Fact]
    public async Task OperationCanceledException_FromWork_SurfacesAsCanceledTask()
    {
        SkipIfNotWindows();

        using var scheduler = new SingleThreadedApartmentTaskScheduler();
        using var cts = new CancellationTokenSource();
#if NET8_0_OR_GREATER
        await cts.CancelAsync();
#else
        cts.Cancel();
#endif

#pragma warning disable VSTHRD003
        var task = scheduler.RunAsync<int>(
            () =>
            {
                cts.Token.ThrowIfCancellationRequested();
                return 0;
            },
            cts.Token);

        var act = async () => await task;
#pragma warning restore VSTHRD003
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Cancellation_DuringExecution_SurfacesAsCanceledTask()
    {
        SkipIfNotWindows();

        using var scheduler = new SingleThreadedApartmentTaskScheduler();
        using var cts = new CancellationTokenSource();
        using var started = new ManualResetEventSlim(initialState: false);

#pragma warning disable VSTHRD003
        var task = scheduler.RunAsync<int>(
            () =>
            {
                started.Set();

                // User code cooperatively observes the caller's token.
                while (!cts.Token.IsCancellationRequested)
                {
#pragma warning disable S2925
                    Thread.Sleep(5);
#pragma warning restore S2925
                }

                // Return a normal value; the scheduler should still surface this as canceled
                // because the cancellation token on the work item was signaled during execution.
                return 123;
            },
            cts.Token);
#pragma warning restore VSTHRD003

#pragma warning disable xUnit1051
        started.Wait(TimeSpan.FromSeconds(2)).Should().BeTrue();
#pragma warning restore xUnit1051
#if NET8_0_OR_GREATER
        await cts.CancelAsync();
#else
        cts.Cancel();
#endif

#pragma warning disable VSTHRD003
        var act = async () => await task;
#pragma warning restore VSTHRD003
        await act.Should().ThrowAsync<OperationCanceledException>();
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
        // For older .NET versions, use PlatformID
        return Environment.OSVersion.Platform == PlatformID.Win32NT
               || Environment.OSVersion.Platform == PlatformID.Win32S
               || Environment.OSVersion.Platform == PlatformID.Win32Windows
               || Environment.OSVersion.Platform == PlatformID.WinCE;
#endif
    }
}
