using System;
using System.Threading;

namespace AdaskoTheBeAsT.Interop.Threading;

public sealed class StaYield(int intervalMs = 15)
{
    private int _last = Environment.TickCount;

    /// <summary>
    /// Call this inside long loops. Pumps messages if the interval passed.
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
    /// Helpful when you're waiting on a condition without blocking.
    /// </summary>
    /// <param name="condition"></param>
    /// <param name="checkEveryMs"></param>
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
    /// Sleep without starving the message loop.
    /// </summary>
    /// <param name="ms"></param>
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
