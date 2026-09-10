#if NET8_0_OR_GREATER
using System.Runtime.Versioning;
#endif
using System.Threading.Tasks;
using AwesomeAssertions;
using Xunit;

namespace AdaskoTheBeAsT.Interop.Threading.Test;

#if NET8_0_OR_GREATER
[SupportedOSPlatform("windows")]
#endif
public class StaYieldTest
{
    [Fact]
    public void Occasionally_IsSafeToCall_Often()
    {
        var y = new StaYield();
        for (int i = 0; i < 20; i++)
        {
            y.Occasionally();
        }

        true.Should().BeTrue(); // no exceptions
    }

    [Fact]
    public void SpinUntil_Returns_WhenConditionTrue()
    {
#pragma warning disable ParallelChecker
        var y = new StaYield();
        var flag = false;

        _ = Task.Run(
            async () =>
            {
                await Task.Delay(50, TestContext.Current.CancellationToken);
                flag = true;
            },
            TestContext.Current.CancellationToken);
        y.SpinUntil(() => flag, TestContext.Current.CancellationToken, 5);
        flag.Should().BeTrue();
#pragma warning restore ParallelChecker
    }

    [Fact]
    public void Sleep_DoesNotThrow()
    {
        var y = new StaYield();
#pragma warning disable xUnit1051, MA0040 // Exercise the existing non-cancelable overload.
        y.Sleep(25);
#pragma warning restore xUnit1051, MA0040
        true.Should().BeTrue();
    }
}
