using System;
using System.Diagnostics;
using AwesomeAssertions;
using Xunit;

namespace AdaskoTheBeAsT.Interop.Threading.Test;

public class StaYieldAdditionalTest
{
    [Fact]
    public void Ctor_WithNonPositiveInterval_ClampsToOne()
    {
        // Values <= 0 are clamped via Math.Max(1, ...). Exercising Occasionally()
        // on a freshly-constructed instance must not throw; if the clamp were
        // missing (interval <= 0), the underlying MillisecondsToTicks math would
        // return 0 and the first Occasionally() call would still behave safely,
        // but any ctor-level arithmetic on non-positive ms would have thrown
        // before we got here.
        var actZero = Record.Exception(() =>
        {
            var y = new StaYield(0);
            y.Occasionally();
        });
        var actNegative = Record.Exception(() =>
        {
            var y2 = new StaYield(-5);
            y2.Occasionally();
        });

        actZero.Should().BeNull();
        actNegative.Should().BeNull();
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

        // Observable "immediate return": an already-true condition causes
        // SpinUntil to exit without calling Thread.Sleep(checkEveryMs). We
        // bound the elapsed time well below the configured poll interval
        // (500 ms) so that a regressing implementation that ran one extra
        // iteration would be detected.
        var stopwatch = Stopwatch.StartNew();
        y.SpinUntil(() => true, checkEveryMs: 500);
        stopwatch.Stop();

        stopwatch.ElapsedMilliseconds.Should().BeLessThan(250);
    }

    [Fact]
    public void Sleep_ZeroOrNegative_ReturnsImmediately()
    {
        var y = new StaYield();

        // Observable "immediate return": Sleep(0) / Sleep(-1) must not block.
        // A significant wall-clock delay indicates a regression.
        var stopwatch = Stopwatch.StartNew();
        y.Sleep(0);
        y.Sleep(-1);
        stopwatch.Stop();

        stopwatch.ElapsedMilliseconds.Should().BeLessThan(50);
    }
}
