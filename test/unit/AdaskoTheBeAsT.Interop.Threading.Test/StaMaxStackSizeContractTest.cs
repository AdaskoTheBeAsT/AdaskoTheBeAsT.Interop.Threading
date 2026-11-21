using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
#if NET8_0_OR_GREATER
using System.Runtime.Versioning;
#endif
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Xunit;

namespace AdaskoTheBeAsT.Interop.Threading.Test;

#pragma warning disable VSTHRD003, VSTHRD103, S6966 // Exercising both Run and RunAsync with custom stack sizes.
#if NET8_0_OR_GREATER
[SupportedOSPlatform("windows")]
#endif
public class StaMaxStackSizeContractTest
{
    private const int EightMegabytes = 8 * 1024 * 1024;
    private const int StackFrameSize = 32 * 1024;

    // 80 frames of 32 KB each exceed the default thread stack on x86 and x64.
    private const int StackDepth = 80;

    [Fact]
    public async Task NegativeMaxStackSize_ThrowsArgumentOutOfRangeExceptionAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var schedulerAct = () =>
        {
            using var scheduler = new SingleThreadedApartmentTaskScheduler(
                new SingleThreadedApartmentTaskSchedulerOptions { MaxStackSize = -1 });
        };
        schedulerAct.Should().Throw<ArgumentOutOfRangeException>().Which.ParamName.Should().Be("options");

        var syncAct = () => SingleThreadedApartmentTask.Run(
            static () => 1,
            new SingleThreadedApartmentTaskOptions { MaxStackSize = -1 },
            ct);
        syncAct.Should().Throw<ArgumentOutOfRangeException>().Which.ParamName.Should().Be("options");

        var asyncAct = async () => await SingleThreadedApartmentTask.RunAsync(
            static () => 1,
            new SingleThreadedApartmentTaskOptions { MaxStackSize = -1 },
            ct);
        (await asyncAct.Should().ThrowAsync<ArgumentOutOfRangeException>())
            .Which.ParamName.Should().Be("options");
    }

    [Fact]
    public async Task CustomMaxStackSize_SupportsDeepStackFramesOnSchedulerAndOneOffRunAsync()
    {
        var ct = TestContext.Current.CancellationToken;

        using (var scheduler = new SingleThreadedApartmentTaskScheduler(
                   new SingleThreadedApartmentTaskSchedulerOptions
                   {
                       ThreadName = "DeepStack-Scheduler-STA",
                       MaxStackSize = EightMegabytes,
                   }))
        {
            var schedulerChecksum = await scheduler.RunAsync(static () => ConsumeManagedStack(StackDepth), ct);
            schedulerChecksum.Should().BePositive();
        }

        var checksum = await SingleThreadedApartmentTask.RunAsync(
            static () => ConsumeManagedStack(StackDepth),
            new SingleThreadedApartmentTaskOptions
            {
                ThreadName = "DeepStack-Async-STA",
                MaxStackSize = EightMegabytes,
            },
            ct);
        checksum.Should().BePositive();
    }

    [Fact]
    public void CustomMaxStackSize_SupportsDeepStackFramesOnOneOffRun()
    {
        var checksum = SingleThreadedApartmentTask.Run(
            static () => ConsumeManagedStack(StackDepth),
            new SingleThreadedApartmentTaskOptions
            {
                ThreadName = "DeepStack-Sync-STA",
                MaxStackSize = EightMegabytes,
            },
            TestContext.Current.CancellationToken);
        checksum.Should().BePositive();
    }

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    private static long ConsumeManagedStack(int remainingDepth)
    {
        var frame = default(StackFrame);
        frame.Seed(remainingDepth);

        if (remainingDepth <= 1)
        {
            return frame.ReadSum();
        }

        return frame.ReadSum() + ConsumeManagedStack(remainingDepth - 1);
    }

    [StructLayout(LayoutKind.Explicit, Size = StackFrameSize)]
    private struct StackFrame
    {
        [FieldOffset(0)]
        private long _first;

        [FieldOffset(StackFrameSize - sizeof(long))]
        private long _last;

        public void Seed(int depth)
        {
            _first = depth;
            _last = depth ^ 0x5A;
        }

        public readonly long ReadSum()
        {
            return _first + _last;
        }
    }
}
