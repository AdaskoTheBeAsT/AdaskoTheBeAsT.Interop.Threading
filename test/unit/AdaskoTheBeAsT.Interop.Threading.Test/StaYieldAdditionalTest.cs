using System;
using AwesomeAssertions;
using Xunit;

namespace AdaskoTheBeAsT.Interop.Threading.Test;

public class StaYieldAdditionalTest
{
    [Fact]
    public void Ctor_WithNonPositiveInterval_ClampsToOne()
    {
        // Values <= 0 are clamped via Math.Max(1, ...). Must not throw.
        var y = new StaYield(0);
        y.Occasionally();

        var y2 = new StaYield(-5);
        y2.Occasionally();

        true.Should().BeTrue();
    }

    [Fact]
    public void SpinUntil_NullCondition_Throws()
    {
        var y = new StaYield();

        var act = () => y.SpinUntil(null!, 1);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void SpinUntil_ConditionAlreadyTrue_ReturnsImmediately()
    {
        var y = new StaYield();

        y.SpinUntil(() => true, 1);

        true.Should().BeTrue();
    }

    [Fact]
    public void Sleep_ZeroOrNegative_ReturnsImmediately()
    {
        var y = new StaYield();

        y.Sleep(0);
        y.Sleep(-1);

        true.Should().BeTrue();
    }
}
