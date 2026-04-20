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
public class SingleThreadedApartmentTaskCoverageTest
{
    [Fact]
    public async Task RunAsync_StaYieldOverload_ExecutesOnStaAndReturnsValueAsync()
    {
        SkipIfNotWindows();

        // Exercises the Func<StaYield, T> overload (lines 22-30 of
        // SingleThreadedApartmentTask.cs) which is the "create fresh StaYield
        // per call" static entry point that the scheduler's instance API
        // wraps around.
        var result = await SingleThreadedApartmentTask.RunAsync<int>(
            yield =>
            {
                yield.Should().NotBeNull();
                Thread.CurrentThread.GetApartmentState().Should().Be(ApartmentState.STA);
                return 321;
            },
            CancellationToken.None);

        result.Should().Be(321);
    }

    [Fact]
    public Task RunAsync_StaYieldOverload_NullFunc_ThrowsAsync()
    {
        SkipIfNotWindows();

        Func<Task> act = async () => await SingleThreadedApartmentTask.RunAsync<int>(
            (Func<StaYield, int>)null!,
            CancellationToken.None);

        return act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public Task RunAsync_FuncOverload_NullFunc_ThrowsAsync()
    {
        SkipIfNotWindows();

        // Exercises the null-guard branch of RunAsync<T>(Func<T>, ct)
        // (lines 49-50 of SingleThreadedApartmentTask.cs).
        Func<Task> act = async () => await SingleThreadedApartmentTask.RunAsync<int>(
            (Func<int>)null!,
            CancellationToken.None);

        return act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task RunAsync_StaYieldOverload_PreCanceledToken_ReturnsCanceledTaskAsync()
    {
        SkipIfNotWindows();

        using var cts = new CancellationTokenSource();
#if NET8_0_OR_GREATER
        await cts.CancelAsync();
#else
        cts.Cancel();
#endif

        var act = async () => await SingleThreadedApartmentTask.RunAsync<int>(
            _ => 1,
            cts.Token);

        await act.Should().ThrowAsync<TaskCanceledException>();
    }

    [Fact]
    public async Task RunWithTimeoutAsync_CompletesBeforeTimeout_ReturnsResultAsync()
    {
        SkipIfNotWindows();

        // Exercises RunWithTimeoutAsync happy path (line 106 of
        // SingleThreadedApartmentTask.cs) which chains RunAsync ->
        // TimeoutAfterAsync when the delegate completes within the budget.
        var result = await SingleThreadedApartmentTask.RunWithTimeoutAsync<int>(
            TimeSpan.FromSeconds(5),
            () => 99,
            CancellationToken.None);

        result.Should().Be(99);
    }

    [Fact]
    public Task RunWithTimeoutAsync_DelegateExceedsBudget_ThrowsTimeoutExceptionAsync()
    {
        SkipIfNotWindows();

        // Exercises the path where TimeoutAfterAsync fires before the STA
        // delegate returns.
        Func<Task> act = async () => await SingleThreadedApartmentTask.RunWithTimeoutAsync<int>(
            TimeSpan.FromMilliseconds(50),
            () =>
            {
#pragma warning disable S2925
                Thread.Sleep(500);
#pragma warning restore S2925
                return 1;
            },
            CancellationToken.None);

        return act.Should().ThrowAsync<TimeoutException>();
    }

    private static void SkipIfNotWindows()
    {
        if (!IsWindows())
        {
            throw new PlatformNotSupportedException("STA task relies on Win32 message pumping; test is Windows-only.");
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
