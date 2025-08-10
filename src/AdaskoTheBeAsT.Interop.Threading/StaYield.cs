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
        if (Environment.TickCount - _last >= intervalMs)
        {
            NativeMethods.PumpPendingMessages();
            _last = Environment.TickCount;
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
        var end = Environment.TickCount + ms;
        while (Environment.TickCount < end)
        {
            Occasionally();
            Thread.Sleep(Math.Min(10, end - Environment.TickCount));
        }
    }
}
