using System;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Xunit;

namespace AdaskoTheBeAsT.Interop.Threading.Test;

public class TaskExtensionTest
{
    [Fact]
    public Task TimeoutAfterAsync_TimesOutAsync()
    {
        var never = new TaskCompletionSource<int>();
        Func<Task> act = async () =>
        {
#pragma warning disable VSTHRD003
            _ = await never.Task.TimeoutAfterAsync(TimeSpan.FromMilliseconds(50), CancellationToken.None);
#pragma warning restore VSTHRD003
        };

        return act.Should().ThrowAsync<TimeoutException>();
    }

    [Fact]
    public async Task TimeoutAfterAsync_PropagatesResult_WhenInTimeAsync()
    {
#if NET8_0_OR_GREATER
        var ct = TestContext.Current.CancellationToken;
#else
        var ct = CancellationToken.None;
#endif

        var t = Task.Run(
            async () =>
            {
                await Task.Delay(20, ct);
                return 99;
            },
            ct);

        var result = await t.TimeoutAfterAsync(TimeSpan.FromMilliseconds(200), ct);
        result.Should().Be(99);
    }

    [Fact]
    public async Task TimeoutAfterAsync_PropagatesCancellationAsync()
    {
        using var cts = new CancellationTokenSource();
        var never = new TaskCompletionSource<int>();

        cts.CancelAfter(50);

        Func<Task> act = async () =>
        {
#pragma warning disable VSTHRD003
            _ = await never.Task.TimeoutAfterAsync(TimeSpan.FromSeconds(5), cts.Token);
#pragma warning restore VSTHRD003
        };

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
