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

        var id = await SingleThreadedApartmentTaskScheduler.RunAsync(
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

        var observed = 0;
        await SingleThreadedApartmentTaskScheduler.RunAsync<object?>(
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

        var step = 0;
        await SingleThreadedApartmentTaskScheduler.RunAsync<object?>(
            () =>
            {
                step.Should().Be(0);
                step = 1;
                return null;
            },
            CancellationToken.None);

        await SingleThreadedApartmentTaskScheduler.RunAsync<object?>(
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

        var first = SingleThreadedApartmentTaskScheduler.RunAsync<object?>(() => null, CancellationToken.None);
        var second = SingleThreadedApartmentTaskScheduler.RunAsync<object?>(() => null, CancellationToken.None);

        var combined = Task.WhenAll(first, second);
        var completed = await Task.WhenAny(combined, Task.Delay(TimeSpan.FromSeconds(5)));

        ReferenceEquals(combined, completed).Should().BeTrue("both items should complete promptly when the pump runs after each item");
    }

    [Fact]
    public void RunAsync_FastFails_WhenAlreadyCanceled()
    {
        SkipIfNotWindows();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var task = SingleThreadedApartmentTaskScheduler.RunAsync(() => 1, cts.Token);
        task.IsCanceled.Should().BeTrue();
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
