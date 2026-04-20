using System;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Xunit;

namespace AdaskoTheBeAsT.Interop.Threading.Test;

public class TaskExtensionCoverageTest
{
    [Fact]
    public async Task TimeoutAfterAsync_NegativeTimeout_ThrowsArgumentOutOfRangeAsync()
    {
        var completed = Task.FromResult(1);

#pragma warning disable VSTHRD003
        var act = async () => await completed.TimeoutAfterAsync(
            TimeSpan.FromMilliseconds(-5),
            CancellationToken.None);
#pragma warning restore VSTHRD003

        var assertion = await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
        assertion.And.ParamName.Should().Be("timeout");
    }

    [Fact]
    public async Task TimeoutAfterAsync_TooLargeTimeout_ThrowsArgumentOutOfRangeAsync()
    {
        var completed = Task.FromResult(1);
        var tooLarge = TimeSpan.FromMilliseconds((double)int.MaxValue + 10.0);

#pragma warning disable VSTHRD003
        var act = async () => await completed.TimeoutAfterAsync(
            tooLarge,
            CancellationToken.None);
#pragma warning restore VSTHRD003

        var assertion = await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
        assertion.And.ParamName.Should().Be("timeout");
    }

    [Fact]
    public async Task TimeoutAfterAsync_InfiniteTimeSpan_CompletesWithResultAsync()
    {
        // Exercises the Timeout.InfiniteTimeSpan branch which skips the
        // upfront range validation.
        var t = Task.FromResult(456);

#pragma warning disable VSTHRD003
        var result = await t.TimeoutAfterAsync(
            Timeout.InfiniteTimeSpan,
            CancellationToken.None);
#pragma warning restore VSTHRD003

        result.Should().Be(456);
    }

    [Fact]
    public async Task TimeoutAfterAsync_TaskAlreadyCompleted_ReturnsImmediatelyAsync()
    {
        // Exercises the "finished == task" fast branch where timeoutCts is
        // canceled up front and the already-completed task's result is
        // returned without a wall-clock delay.
        var t = Task.FromResult(999);

#pragma warning disable VSTHRD003
        var result = await t.TimeoutAfterAsync(
            TimeSpan.FromSeconds(10),
            CancellationToken.None);
#pragma warning restore VSTHRD003

        result.Should().Be(999);
    }

    [Fact]
    public async Task TimeoutAfterAsync_PropagatesFaultFromTaskAsync()
    {
        var t = Task.FromException<int>(new InvalidOperationException("underlying"));

#pragma warning disable VSTHRD003
        var act = async () => await t.TimeoutAfterAsync(
            TimeSpan.FromSeconds(5),
            CancellationToken.None);
#pragma warning restore VSTHRD003

        var assertion = await act.Should().ThrowAsync<InvalidOperationException>();
        assertion.And.Message.Should().Be("underlying");
    }

    [Fact]
    public async Task TimeoutAfterAsync_CallerTokenCanceled_SurfacesOperationCanceledAsync()
    {
        // Pre-cancel the caller's token so the internal Task.Delay completes
        // synchronously as canceled (Task.WhenAny's fast path), driving the
        // method into the timeout-wins branch where the subsequent
        // cancellationToken.ThrowIfCancellationRequested() gate must surface
        // OperationCanceledException (not TimeoutException). A generous 10s
        // timeout guarantees the cancellation signal is the ONLY thing that
        // can terminate the Task.Delay first, making this deterministic on
        // CI runners with unpredictable scheduling jitter.
        using var cts = new CancellationTokenSource();
        var never = new TaskCompletionSource<int>();
#if NET8_0_OR_GREATER
        await cts.CancelAsync();
#else
        cts.Cancel();
#endif

#pragma warning disable VSTHRD003
        var act = async () => await never.Task.TimeoutAfterAsync(
            TimeSpan.FromSeconds(10),
            cts.Token);
#pragma warning restore VSTHRD003

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
