using System;
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
public class SingleThreadedApartmentTaskSchedulerLifecycleTest
{
    [Fact]
    public async Task RunAsync_CanceledWait_ObservesLaterDelegateFaultAsync()
    {
        var marker = Guid.NewGuid().ToString();
        var unobserved = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<UnobservedTaskExceptionEventArgs> handler = (_, args) =>
        {
            if (string.Equals(args.Exception.InnerException?.Message, marker, StringComparison.Ordinal))
            {
                unobserved.TrySetResult(true);
                args.SetObserved();
            }
        };

        TaskScheduler.UnobservedTaskException += handler;
        try
        {
            await RunCanceledWorkThatFaultsAsync(marker);

            // Finalization is the behavior under test. Only this test's unique
            // exception is observed, so unrelated tasks cannot affect the result.
#pragma warning disable S1215
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
#pragma warning restore S1215
            unobserved.Task.IsCompleted.Should().BeFalse();
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= handler;
        }
    }

    // These tests deliberately overlap work with disposal. Finally blocks release
    // blocked delegates before disposing the scheduler and their wait handles.
#pragma warning disable AsyncFixer04, VSTHRD003, IDISP016, IDISP017
    [Fact]
    public async Task Dispose_AfterSelfDisposal_WaitsForThreadExitAsync()
    {
        using var release = new ManualResetEventSlim(initialState: false);
        var selfDisposed = new TaskCompletionSource<Thread>(TaskCreationOptions.RunContinuationsAsynchronously);
        var disposeStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var scheduler = new SingleThreadedApartmentTaskScheduler();
        try
        {
            var work = scheduler.RunAsync(
                () =>
                {
                    scheduler.Dispose();
                    selfDisposed.TrySetResult(Thread.CurrentThread);
                    release.Wait(TimeSpan.FromSeconds(10), CancellationToken.None);
                    return 0;
                },
                CancellationToken.None);

            var thread = await selfDisposed.Task.TimeoutAfterAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
            var disposal = Task.Run(
                () =>
                {
                    disposeStarted.TrySetResult(true);
                    scheduler.Dispose();
                    return thread.IsAlive;
                },
                CancellationToken.None);

            await disposeStarted.Task.TimeoutAfterAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
            var observation = Task.Delay(100, CancellationToken.None);
            var completed = await Task.WhenAny(disposal, observation);
            completed.Should().BeSameAs(observation, "external disposal must wait for the running STA delegate");

            release.Set();
            var isAlive = await disposal.TimeoutAfterAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
            isAlive.Should().BeFalse();
            var act = async () => await work;
            await act.Should().ThrowAsync<OperationCanceledException>();
        }
        finally
        {
            release.Set();
            scheduler.Dispose();
        }
    }

    [Fact]
    public async Task RunAsync_RacingShutdown_AllAcceptedTasksCompleteAsync()
    {
        for (var iteration = 0; iteration < 25; iteration++)
        {
            using var scheduler = new SingleThreadedApartmentTaskScheduler();
            using var start = new ManualResetEventSlim(initialState: false);
            var submissions = new Task<int>[20];
            var producer = Task.Run(
                () =>
                {
                    start.Wait(TimeSpan.FromSeconds(5), CancellationToken.None);
                    for (var i = 0; i < submissions.Length; i++)
                    {
                        submissions[i] = scheduler.RunAsync(() => 1, CancellationToken.None);
                    }
                },
                CancellationToken.None);
            var shutdown = Task.Run(
                () =>
                {
                    start.Wait(TimeSpan.FromSeconds(5), CancellationToken.None);
                    scheduler.Shutdown();
                },
                CancellationToken.None);

            start.Set();
            await Task.WhenAll(producer, shutdown);
            scheduler.Dispose();

            var all = Task.WhenAll(submissions);
            var completed = await Task.WhenAny(all, Task.Delay(TimeSpan.FromSeconds(5), CancellationToken.None));
            completed.Should().BeSameAs(all, "shutdown must not strand accepted work");
            _ = await Record.ExceptionAsync(async () => await all);
        }
    }

    private static async Task RunCanceledWorkThatFaultsAsync(string marker)
    {
        using var release = new ManualResetEventSlim(initialState: false);
        using var cts = new CancellationTokenSource();
        using var scheduler = new SingleThreadedApartmentTaskScheduler();
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var work = scheduler.RunAsync<int>(
                () =>
                {
                    started.TrySetResult(true);
                    release.Wait(TimeSpan.FromSeconds(10), CancellationToken.None);
                    throw new InvalidOperationException(marker);
                },
                TimeSpan.FromSeconds(30),
                cts.Token);

            await started.Task.TimeoutAfterAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
#if NET8_0_OR_GREATER
            await cts.CancelAsync();
#else
            cts.Cancel();
#endif
            var act = async () => await work;
            await act.Should().ThrowAsync<OperationCanceledException>();
        }
        finally
        {
            release.Set();
        }
    }
#pragma warning restore AsyncFixer04, VSTHRD003, IDISP016, IDISP017
}
