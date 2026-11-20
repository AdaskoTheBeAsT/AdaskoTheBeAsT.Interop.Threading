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
    public async Task CustomMaxStackSize_SupportsDeepStackFramesOnSchedulerAndOneOffStaAsync()
    {
        var ct = TestContext.Current.CancellationToken;

        // 80 frames * 32 KB value-type frame per call = 2.56 MB of active thread stack,
        // exceeding standard 256 KB (IIS) and 1 MB (default PE) thread stacks on both x86 and x64.
        const int targetDepth = 80;

        using (var scheduler = new SingleThreadedApartmentTaskScheduler(
                   new SingleThreadedApartmentTaskSchedulerOptions
                   {
                       ThreadName = "DeepStack-Scheduler-STA",
                       MaxStackSize = EightMegabytes,
                   }))
        {
            var schedulerChecksum = await scheduler.RunAsync(() => ConsumeManagedStack(targetDepth), ct);
            schedulerChecksum.Should().BeGreaterThan(0);
        }

        var syncChecksum = SingleThreadedApartmentTask.Run(
            static () => ConsumeManagedStack(targetDepth),
            new SingleThreadedApartmentTaskOptions
            {
                ThreadName = "DeepStack-Sync-STA",
                MaxStackSize = EightMegabytes,
            },
            ct);
        syncChecksum.Should().BeGreaterThan(0);

        var asyncChecksum = await SingleThreadedApartmentTask.RunAsync(
            static () => ConsumeManagedStack(targetDepth),
            new SingleThreadedApartmentTaskOptions
            {
                ThreadName = "DeepStack-Async-STA",
                MaxStackSize = EightMegabytes,
            },
            ct);
        asyncChecksum.Should().BeGreaterThan(0);
    }

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    private static long ConsumeManagedStack(int remainingDepth)
    {
        var frame = default(Block32K);
        frame.Seed(remainingDepth);

        if (remainingDepth <= 1)
        {
            return frame.ReadSum();
        }

        return frame.ReadSum() + ConsumeManagedStack(remainingDepth - 1);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Block256
    {
        public long V0;
        public long V1;
        public long V2;
        public long V3;
        public long V4;
        public long V5;
        public long V6;
        public long V7;
        public long V8;
        public long V9;
        public long V10;
        public long V11;
        public long V12;
        public long V13;
        public long V14;
        public long V15;
        public long V16;
        public long V17;
        public long V18;
        public long V19;
        public long V20;
        public long V21;
        public long V22;
        public long V23;
        public long V24;
        public long V25;
        public long V26;
        public long V27;
        public long V28;
        public long V29;
        public long V30;
        public long V31;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Block4K
    {
        public Block256 B0;
        public Block256 B1;
        public Block256 B2;
        public Block256 B3;
        public Block256 B4;
        public Block256 B5;
        public Block256 B6;
        public Block256 B7;
        public Block256 B8;
        public Block256 B9;
        public Block256 B10;
        public Block256 B11;
        public Block256 B12;
        public Block256 B13;
        public Block256 B14;
        public Block256 B15;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Block32K
    {
        public Block4K K0;
        public Block4K K1;
        public Block4K K2;
        public Block4K K3;
        public Block4K K4;
        public Block4K K5;
        public Block4K K6;
        public Block4K K7;

        public void Seed(int depth)
        {
            K0.B0.V0 = depth;
            K7.B15.V31 = depth ^ 0x5A;
        }

        public readonly long ReadSum()
        {
            return K0.B0.V0 + K7.B15.V31;
        }
    }
}
