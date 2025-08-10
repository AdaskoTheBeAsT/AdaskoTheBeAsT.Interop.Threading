using System.Threading.Tasks;
using AwesomeAssertions;
using Xunit;

namespace AdaskoTheBeAsT.Interop.Threading.Test;

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

        _ = Task.Run(async () =>
        {
            await Task.Delay(50);
            flag = true;
        });

        y.SpinUntil(() => flag, 5);
        flag.Should().BeTrue();
#pragma warning restore ParallelChecker
    }

    [Fact]
    public void Sleep_DoesNotThrow()
    {
        var y = new StaYield();
        y.Sleep(25);
        true.Should().BeTrue();
    }
}
