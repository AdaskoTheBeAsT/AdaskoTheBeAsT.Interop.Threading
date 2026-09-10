using System;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Xunit;

namespace AdaskoTheBeAsT.Interop.Threading.Test;

// Awaiting source tasks is the contract under test; there is no UI context.
#pragma warning disable VSTHRD003
public class TaskExtensionContractTest
{
    [Theory]
    [InlineData(-2)]
    [InlineData(int.MaxValue)]
    public async Task NonGeneric_InvalidTimeout_IsFaultedAsync(int milliseconds)
    {
        var wait = Task.CompletedTask.TimeoutAfterAsync(TimeSpan.FromMilliseconds(milliseconds), CancellationToken.None);
        (await Record.ExceptionAsync(async () => await wait)).Should().BeOfType<ArgumentOutOfRangeException>();
        wait.IsFaulted.Should().BeTrue();
    }

    [Fact]
    public async Task NonGeneric_NullTask_IsValidatedFirstAsync()
    {
        Task source = null!;
        var wait = source.TimeoutAfterAsync(TimeSpan.FromMilliseconds(-2), CancellationToken.None);
        var error = await Record.ExceptionAsync(async () => await wait);
        error.Should().BeOfType<ArgumentNullException>().Which.ParamName.Should().Be("task");
    }

    [Fact]
    public async Task IncompleteSource_FaultedCancellationException_StaysFaultedAsync()
    {
        var source = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var original = new OperationCanceledException(new CancellationToken(canceled: true));
        var generic = source.Task.TimeoutAfterAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
        var nonGeneric = ((Task)source.Task).TimeoutAfterAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
        source.SetException(original);
        (await Record.ExceptionAsync(async () => await generic)).Should().BeSameAs(original);
        (await Record.ExceptionAsync(async () => await nonGeneric)).Should().BeSameAs(original);
        generic.IsFaulted.Should().BeTrue();
        nonGeneric.IsFaulted.Should().BeTrue();
    }

    [Fact]
    public void UpperBoundaryAndInfiniteTimeout_PreserveCompletedSource()
    {
        var source = Task.FromResult(42);
        source.TimeoutAfterAsync(TimeSpan.FromMilliseconds(int.MaxValue - 1), CancellationToken.None).Should().BeSameAs(source);
        source.TimeoutAfterAsync(Timeout.InfiniteTimeSpan, CancellationToken.None).Should().BeSameAs(source);
    }

    [Fact]
    public async Task NullTask_IsValidatedBeforeTimeoutAsync()
    {
        Task<int> task = null!;
        var act = async () => await task.TimeoutAfterAsync(TimeSpan.FromMilliseconds(-2), CancellationToken.None);
        var error = await act.Should().ThrowAsync<ArgumentNullException>();
        error.Which.ParamName.Should().Be(nameof(task));
    }

    [Fact]
    public async Task CompletedTask_WinsOverPreCanceledWaitAsync()
    {
        var token = new CancellationToken(canceled: true);
        var result = await Task.FromResult(42).TimeoutAfterAsync(TimeSpan.Zero, token);
        result.Should().Be(42);
    }

    [Fact]
    public async Task IncompleteTask_PreCanceledWait_PreservesTokenAsync()
    {
        var source = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var token = new CancellationToken(canceled: true);
        var wait = source.Task.TimeoutAfterAsync(Timeout.InfiniteTimeSpan, token);
        var error = await Record.ExceptionAsync(async () => await wait);
        wait.IsCanceled.Should().BeTrue();
        error.Should().BeAssignableTo<OperationCanceledException>().Which.CancellationToken.Should().Be(token);
        source.Task.IsCompleted.Should().BeFalse();
    }
}
