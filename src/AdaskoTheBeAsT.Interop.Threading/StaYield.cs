using System;
using System.Threading;

namespace AdaskoTheBeAsT.Interop.Threading;

/// <summary>
/// Provides helper methods for long-running work executing on an STA thread.
/// Use these helpers to keep the COM or Windows message pump responsive while work is in progress.
/// </summary>
/// <param name="intervalMs">The minimum interval, in milliseconds, between automatic message-pump checks in <see cref="Occasionally"/>.</param>
public sealed class StaYield(int intervalMs = 15)
{
    private int _last = Environment.TickCount;

    /// <summary>
    /// Pumps pending Windows messages when enough time has elapsed since the previous pump.
    /// Call this from long-running loops on an STA thread to keep message processing responsive.
    /// </summary>
    public void Occasionally()
    {
        var now = Environment.TickCount;
        if (unchecked(now - _last) >= intervalMs)
        {
            NativeMethods.PumpPendingMessages();
            _last = now;
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
        var start = Environment.TickCount;
        while (unchecked(Environment.TickCount - start) < ms)
        {
            Occasionally();
            var remaining = ms - unchecked(Environment.TickCount - start);
            Thread.Sleep(Math.Min(10, Math.Max(1, remaining)));
        }
    }
}
