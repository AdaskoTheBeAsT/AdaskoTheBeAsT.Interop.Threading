using System;
using System.Diagnostics.Tracing;
#if NET8_0_OR_GREATER
using System.Runtime.Versioning;
#endif
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Xunit;

namespace AdaskoTheBeAsT.Interop.Threading.Test;

#pragma warning disable VSTHRD003 // Observing the owned scheduler and listener signals.
#if NET8_0_OR_GREATER
[SupportedOSPlatform("windows")]
#endif
public class StaDiagnosticsContractTest
{
    [Fact]
    public async Task WorkState_ReportsTimingOutsideTheStaAsync()
    {
        using var scheduler = new SingleThreadedApartmentTaskScheduler(
            new SingleThreadedApartmentTaskSchedulerOptions { EnableDiagnostics = true });
        var observed = new TaskCompletionSource<(int ThreadId, int Pending, double QueueMilliseconds)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var listener = new RecordingListener(
            data =>
            {
                if (data.EventId == 1 && string.Equals(data.Payload?[2] as string, "running", StringComparison.Ordinal))
                {
                    observed.TrySetResult((
                        Environment.CurrentManagedThreadId,
                        (int)data.Payload![3]!,
                        (double)data.Payload[4]!));
                }
            });
        var workerThread = await scheduler.RunAsync(() => Environment.CurrentManagedThreadId, CancellationToken.None);
        var (threadId, pending, queueMilliseconds) = await observed.Task.TimeoutAfterAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
        threadId.Should().NotBe(workerThread);
        pending.Should().BeGreaterThanOrEqualTo(0);
        queueMilliseconds.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task WorkerStopped_ObservesPublishedCompletionAsync()
    {
        using var scheduler = new SingleThreadedApartmentTaskScheduler(
            new SingleThreadedApartmentTaskSchedulerOptions { EnableDiagnostics = true });
        var observed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var listener = new RecordingListener(
            data =>
            {
                if (data.EventId == 2)
                {
                    // A listener may wait on Completion. It must already be published.
                    observed.TrySetResult(scheduler.Completion.IsCompleted);
                }
            });
        await scheduler.ShutdownAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
        (await observed.Task.TimeoutAfterAsync(TimeSpan.FromSeconds(5), CancellationToken.None)).Should().BeTrue();
    }

    private sealed class RecordingListener : EventListener
    {
        private readonly Action<EventWrittenEventArgs> _record;

        public RecordingListener(Action<EventWrittenEventArgs> record)
        {
            _record = record;
            EnableEvents(StaSchedulerEventSource.Log, EventLevel.Informational);
        }

        protected override void OnEventWritten(EventWrittenEventArgs eventData)
        {
            _record(eventData);
        }
    }
}
