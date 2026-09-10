using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Runtime.CompilerServices;
#if NET8_0_OR_GREATER
using System.Runtime.Versioning;
#endif
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Xunit;

namespace AdaskoTheBeAsT.Interop.Threading.Test;

// Work deliberately remains pending across awaits; finally always releases the worker.
#pragma warning disable VSTHRD003, AsyncFixer04
#if NET8_0_OR_GREATER
[SupportedOSPlatform("windows")]
#endif
public class StaQueueContractTest
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task ConcurrentProducers_CannotExceedPendingCapacityAsync()
    {
        using var release = new ManualResetEventSlim(initialState: false);
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var scheduler = new SingleThreadedApartmentTaskScheduler(
            new SingleThreadedApartmentTaskSchedulerOptions { MaximumPendingWorkItems = 4 });
        var active = BlockWorkerAsync(scheduler, started, release);
        try
        {
            await started.Task.TimeoutAfterAsync(Budget, CancellationToken.None);
            var work = new ConcurrentBag<Task<int>>();
            await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(
                () => work.Add(scheduler.RunAsync(() => 42, CancellationToken.None)),
                CancellationToken.None)));
            scheduler.PendingWorkItemCount.Should().Be(4);
            var rejected = work.Where(task => task.IsFaulted).ToArray();
            rejected.Should().HaveCount(28);
            foreach (var task in rejected)
            {
                task.Exception!.InnerException.Should().BeOfType<InvalidOperationException>();
            }

            release.Set();
            await active;
            (await Task.WhenAll(work.Except(rejected))).Should().OnlyContain(result => result == 42);
            scheduler.PendingWorkItemCount.Should().Be(0);
        }
        finally
        {
            release.Set();
        }
    }

    [Fact]
    public async Task CanceledPendingWork_DoesNotRetainCapturedPayloadAsync()
    {
        using var release = new ManualResetEventSlim(initialState: false);
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var scheduler = new SingleThreadedApartmentTaskScheduler();
        var active = BlockWorkerAsync(scheduler, started, release);
        try
        {
            await started.Task.TimeoutAfterAsync(Budget, CancellationToken.None);
            var payload = EnqueueAndCancel(scheduler);
#pragma warning disable S1215 // Force collection to prove canceled delegates release their captures.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
#pragma warning restore S1215
            payload.IsAlive.Should().BeFalse();
            scheduler.PendingWorkItemCount.Should().Be(0);
            release.Set();
            await active;
        }
        finally
        {
            release.Set();
        }
    }

    private static Task<int> BlockWorkerAsync(
        SingleThreadedApartmentTaskScheduler scheduler,
        TaskCompletionSource<bool> started,
        ManualResetEventSlim release)
    {
        return scheduler.RunAsync(
            () =>
            {
                started.TrySetResult(true);
                release.Wait(Budget, CancellationToken.None);
                return 0;
            },
            CancellationToken.None);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference EnqueueAndCancel(SingleThreadedApartmentTaskScheduler scheduler)
    {
        var payload = new object();
        var reference = new WeakReference(payload);
        using var cancel = new CancellationTokenSource();
        var pending = scheduler.RunAsync(
            () =>
            {
                GC.KeepAlive(payload);
                return 1;
            },
            cancel.Token);
        cancel.Cancel();
        pending.IsCanceled.Should().BeTrue();
        return reference;
    }
}
