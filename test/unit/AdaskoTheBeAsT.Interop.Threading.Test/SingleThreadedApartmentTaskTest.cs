using System;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Xunit;

namespace AdaskoTheBeAsT.Interop.Threading.Test;

public class SingleThreadedApartmentTaskTest
{
    [Fact]
    public async Task RunAsync_ReturnsResult_OnStaThread()
    {
        var result = await SingleThreadedApartmentTask.RunAsync(
            () =>
            {
                Thread.CurrentThread.GetApartmentState().Should().Be(ApartmentState.STA);
                return 123;
            },
            CancellationToken.None);

        result.Should().Be(123);
    }

    [Fact]
    public Task RunAsync_PropagatesException()
    {
        Func<Task> act = async () =>
            await SingleThreadedApartmentTask.RunAsync<object?>(
                () => throw new InvalidOperationException("boom"),
                CancellationToken.None);

        return act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("boom");
    }

    [Fact]
    public void RunAsync_FastFails_WhenAlreadyCanceled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var task = SingleThreadedApartmentTask.RunAsync(() => 42, cts.Token);
        task.IsCanceled.Should().BeTrue();
    }
}
