using System;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Xunit;

namespace AdaskoTheBeAsT.Interop.Threading.Test;

public class TaskExtensionTest
{
    [Fact]
    public Task TimeoutAfterAsync_TimesOut()
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
    public async Task TimeoutAfterAsync_PropagatesResult_WhenInTime()
    {
        var t = Task.Run(async () =>
        {
            await Task.Delay(20);
            return 99;
        });

        var result = await t.TimeoutAfterAsync(TimeSpan.FromMilliseconds(200), CancellationToken.None);
        result.Should().Be(99);
    }
}
