using System;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Xunit;

namespace AdaskoTheBeAsT.Interop.Threading.Test;

public class SingleThreadedApartmentTaskTest
{
    [Fact]
    public async Task RunAsync_ReturnsResult_OnStaThreadAsync()
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
    public Task RunAsync_PropagatesExceptionAsync()
    {
        Func<Task> act = async () =>
            await SingleThreadedApartmentTask.RunAsync<object?>(
                () => throw new InvalidOperationException("boom"),
                CancellationToken.None);

        return act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("boom");
    }

    [Fact]
    public async Task RunAsync_FastFails_WhenAlreadyCanceledAsync()
    {
        using var cts = new CancellationTokenSource();
#if NET8_0_OR_GREATER
        await cts.CancelAsync();
#else
        cts.Cancel();
#endif
        var act = async () => await SingleThreadedApartmentTask.RunAsync(() => 42, cts.Token);

        await act.Should().ThrowAsync<TaskCanceledException>();
    }
}
