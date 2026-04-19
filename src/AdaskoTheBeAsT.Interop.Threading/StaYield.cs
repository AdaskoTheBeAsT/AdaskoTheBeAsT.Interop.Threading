using System;
using System.Diagnostics;
using System.Threading;

namespace AdaskoTheBeAsT.Interop.Threading;

/// <summary>
/// Provides helper methods for long-running work executing on an STA thread.
/// Use these helpers to keep the COM or Windows message pump responsive while work is in progress.
/// </summary>
/// <param name="intervalMs">The minimum interval, in milliseconds, between automatic message-pump checks in <see cref="Occasionally"/>.</param>
public sealed class StaYield(int intervalMs = 15)
{
    private readonly long _intervalTicks = Math.Max(1, intervalMs) * (Stopwatch.Frequency / 1000L);
    private long _lastPumpTicks = Stopwatch.GetTimestamp();

    /// <summary>
    /// Pumps pending Windows messages when enough time has elapsed since the previous pump.
    /// Call this from long-running loops on an STA thread to keep message processing responsive.
    /// </summary>
    public void Occasionally()
    {
        var now = Stopwatch.GetTimestamp();
        if (now - _lastPumpTicks >= _intervalTicks)
        {
            NativeMethods.PumpPendingMessages();
            _lastPumpTicks = now;
        }
    }

    /// <summary>
    /// Repeatedly evaluates a condition until it becomes <see langword="true"/>, while continuing to pump messages between checks.
    /// </summary>
    /// <param name="condition">The condition to evaluate.</param>
    /// <param name="checkEveryMs">The delay, in milliseconds, between condition checks.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="condition"/> is <see langword="null"/>.</exception>
    public void SpinUntil(Func<bool> condition, int checkEveryMs = 10)
    {
        if (condition == null)
        {
            throw new ArgumentNullException(nameof(condition));
        }

        while (!condition())
        {
            Occasionally();
            Thread.Sleep(checkEveryMs);
        }
    }

    /// <summary>
    /// Waits for the specified duration without starving the STA message loop.
    /// </summary>
    /// <param name="ms">The number of milliseconds to wait.</param>
    public void Sleep(int ms)
    {
        if (ms <= 0)
        {
            return;
        }

        var targetTicks = Stopwatch.GetTimestamp() + (ms * (Stopwatch.Frequency / 1000L));
        while (true)
        {
            var now = Stopwatch.GetTimestamp();
            var remainingTicks = targetTicks - now;
            if (remainingTicks <= 0)
            {
                return;
            }

            Occasionally();

            var remainingMs = (int)(remainingTicks * 1000L / Stopwatch.Frequency);
            Thread.Sleep(Math.Min(10, Math.Max(1, remainingMs)));
        }
    }
}
