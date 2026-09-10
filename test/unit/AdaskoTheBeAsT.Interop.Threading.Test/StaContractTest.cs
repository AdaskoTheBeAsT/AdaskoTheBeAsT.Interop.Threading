using System;
#if NET8_0_OR_GREATER
using System.Runtime.Versioning;
#endif
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Xunit;

namespace AdaskoTheBeAsT.Interop.Threading.Test;

// Awaiting the scheduler task is the contract under test.
#pragma warning disable VSTHRD003
#if NET8_0_OR_GREATER
[SupportedOSPlatform("windows")]
#endif
public class StaContractTest
{
    [Fact]
    public void InvalidPollingInterval_DoesNotEvaluateCondition()
    {
        var invoked = false;
        var yield = new StaYield();
        var act = () => yield.SpinUntil(
            () =>
            {
                invoked = true;
                return true;
            },
            checkEveryMs: -1);
        act.Should().Throw<ArgumentOutOfRangeException>();
        invoked.Should().BeFalse();
    }

    [Fact]
    public void OversizedDefaultTimeout_IsRejected()
    {
        var act = () =>
        {
            using var scheduler = new SingleThreadedApartmentTaskScheduler(
                new SingleThreadedApartmentTaskSchedulerOptions
                {
                    DefaultWorkItemTimeout = TimeSpan.FromMilliseconds(int.MaxValue),
                });
        };
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task UnrelatedCancellationException_IsFaultedAsync()
    {
        using var scheduler = new SingleThreadedApartmentTaskScheduler();
        var original = new OperationCanceledException(new CancellationToken(canceled: true));
        var task = scheduler.RunAsync<int>(() => throw original, CancellationToken.None);
        var error = await Record.ExceptionAsync(async () => await task);
        task.IsFaulted.Should().BeTrue();
        error.Should().BeSameAs(original);
    }
}
