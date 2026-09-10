using System;
using System.Runtime.InteropServices;
#if NET8_0_OR_GREATER
using System.Runtime.Versioning;
#endif
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Xunit;

namespace AdaskoTheBeAsT.Interop.Threading.Test;

#pragma warning disable VSTHRD003, AsyncFixer04 // Explicitly ordered native integration work.
#if NET8_0_OR_GREATER
[SupportedOSPlatform("windows")]
#endif
public class MessagePumpContractTest
{
    [Fact]
    public void NativeHost_UsesRequestedArchitecture()
    {
        var expected = Environment.GetEnvironmentVariable("ASTRA_TEST_ARCHITECTURE");
        if (string.Equals(expected, "x86", StringComparison.Ordinal))
        {
            IntPtr.Size.Should().Be(4);
        }
        else if (string.Equals(expected, "x64", StringComparison.Ordinal))
        {
            IntPtr.Size.Should().Be(8);
        }
    }

    [Fact]
    public async Task SustainedMessages_DoNotStarveQueuedWorkAsync()
    {
        using var scheduler = new SingleThreadedApartmentTaskScheduler();
        var window = await scheduler.RunAsync(
            () =>
            {
                var created = new MessageWindow { Repost = true };
                created.Post();
                return created;
            },
            CancellationToken.None);
        try
        {
            var received = await scheduler.RunAsync(() => window!.Received, TimeSpan.FromSeconds(5), CancellationToken.None);
            received.Should().BePositive();
            var count = await scheduler.RunCooperativeAsync(
                (yield, token) =>
                {
                    yield.Sleep(30, token);
                    return window!.Received;
                },
                TimeSpan.FromSeconds(5),
                CancellationToken.None);
            count.Should().BeGreaterThan(received);
        }
        finally
        {
            await scheduler.RunAsync(
                () =>
                {
                    window!.Repost = false;
                    window.Dispose();
                    return 0;
                },
                CancellationToken.None);
        }
    }

    [Theory]
    [InlineData(42)]
    [InlineData(-1)]
    public async Task Quit_StopsSchedulerAndCancelsPendingWorkAsync(int exitCode)
    {
        using var release = new ManualResetEventSlim(initialState: false);
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var scheduler = new SingleThreadedApartmentTaskScheduler();
        try
        {
            var active = scheduler.RunAsync(
                () =>
                {
                    started.TrySetResult(true);
                    release.Wait(TimeSpan.FromSeconds(5), CancellationToken.None);
                    NativeMethods.PostQuitMessage(exitCode);
                    return 0;
                },
                CancellationToken.None);
            await started.Task.TimeoutAfterAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
            var pending = scheduler.RunAsync(() => 1, CancellationToken.None);
            release.Set();
            await active;
            await scheduler.Completion.TimeoutAfterAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
            pending.IsCanceled.Should().BeTrue();
            scheduler.QuitExitCode.Should().Be(exitCode);
        }
        finally
        {
            release.Set();
        }
    }

    private static class NativeMethods
    {
#pragma warning disable SYSLIB1054
        [DllImport("user32.dll", ExactSpelling = true)]
        public static extern void PostQuitMessage(int exitCode);
#pragma warning restore SYSLIB1054
    }
}
