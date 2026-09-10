using System;
using System.Diagnostics;
#if NET8_0_OR_GREATER
using System.Runtime.Versioning;
#endif
using System.Threading;

namespace AdaskoTheBeAsT.Interop.Threading;

/// <summary>
/// Provides helper methods for long-running work executing on an STA thread.
/// Use these helpers to keep the COM or Windows message pump responsive while work is in progress.
/// </summary>
/// <param name="intervalMs">The minimum interval, in milliseconds, between automatic message-pump checks in <see cref="Occasionally"/>.</param>
#if NET8_0_OR_GREATER
[SupportedOSPlatform("windows")]
#endif
public sealed class StaYield(int intervalMs = 15)
{
    private readonly long _intervalTicks = MillisecondsToTicks(Math.Max(1, intervalMs));
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
        ValidatePollingInterval(checkEveryMs);
#if NET6_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(condition);
#else
        if (condition == null)
        {
            throw new ArgumentNullException(nameof(condition));
        }
#endif

        while (!condition())
        {
            Occasionally();
            Thread.Sleep(checkEveryMs);
        }
    }

    /// <summary>Waits for a condition while pumping, until canceled.</summary>
    /// <param name="condition">Synchronous condition evaluated on the calling thread.</param>
    /// <param name="cancellationToken">Cancels the cooperative wait.</param>
    /// <param name="checkEveryMs">Non-negative polling interval.</param>
    public void SpinUntil(Func<bool> condition, CancellationToken cancellationToken, int checkEveryMs = 10)
    {
        _ = SpinUntil(condition, Timeout.InfiniteTimeSpan, cancellationToken, checkEveryMs);
    }

    /// <summary>Waits for a condition while pumping. Returns false when the budget expires.</summary>
    /// <param name="condition">Synchronous condition evaluated on the calling thread.</param>
    /// <param name="timeout">Maximum wait, or <see cref="Timeout.InfiniteTimeSpan"/>.</param>
    /// <param name="cancellationToken">Cancels the cooperative wait.</param>
    /// <param name="checkEveryMs">Non-negative polling interval.</param>
    /// <returns>Whether the condition became true.</returns>
    public bool SpinUntil(Func<bool> condition, TimeSpan timeout, CancellationToken cancellationToken, int checkEveryMs = 10)
    {
        ValidatePollingInterval(checkEveryMs);
        TimeoutValidation.Validate(timeout, nameof(timeout));
#if NET8_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(condition);
#else
        if (condition == null)
        {
            throw new ArgumentNullException(nameof(condition));
        }
#endif
        var watch = Stopwatch.StartNew();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (condition())
            {
                return true;
            }

            if (timeout != Timeout.InfiniteTimeSpan && watch.Elapsed >= timeout)
            {
                return false;
            }

            var wait = timeout == Timeout.InfiniteTimeSpan
                ? checkEveryMs
                : Math.Min(checkEveryMs, Math.Max(0, (int)(timeout - watch.Elapsed).TotalMilliseconds));
            Sleep(wait, cancellationToken);
            Occasionally();
        }
    }

    /// <summary>Waits on the executing thread while pumping messages and observing cancellation.</summary>
    /// <param name="ms">Non-negative duration in milliseconds.</param>
    /// <param name="cancellationToken">Cancels the cooperative wait.</param>
    public void Sleep(int ms, CancellationToken cancellationToken)
    {
        if (ms < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ms), ms, "Sleep duration must be non-negative.");
        }

        var watch = Stopwatch.StartNew();
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            Occasionally();
            var remaining = ms - watch.ElapsedMilliseconds;
            if (remaining <= 0)
            {
                return;
            }

            // Bounded sleeps avoid allocating a token's native wait handle.
            Thread.Sleep((int)Math.Min(10, remaining));
        }
        while (true);
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

        var targetTicks = Stopwatch.GetTimestamp() + MillisecondsToTicks(ms);
        while (true)
        {
            var now = Stopwatch.GetTimestamp();
            var remainingTicks = targetTicks - now;
            if (remainingTicks <= 0)
            {
                return;
            }

            Occasionally();

            // Convert remaining ticks to milliseconds using overflow-safe math.
            // We only need to know whether the remainder is >= 10 ms (in which
            // case we sleep a fixed 10 ms) or a small tail (sleep 1..9 ms), so
            // avoid the long multiplication entirely for the common "far from
            // deadline" case that would otherwise overflow on long waits.
            var tenMsTicks = MillisecondsToTicks(10);
            int sleepMs;
            if (remainingTicks >= tenMsTicks)
            {
                sleepMs = 10;
            }
            else
            {
                var remainingMsDouble = (double)remainingTicks * 1000.0 / Stopwatch.Frequency;
                sleepMs = remainingMsDouble >= 10.0
                    ? 10
                    : Math.Max(1, (int)remainingMsDouble);
            }

            Thread.Sleep(sleepMs);
        }
    }

    // Converts milliseconds to Stopwatch ticks with full precision and clamps the
    // result to at least one tick so callers never get a "zero-interval" threshold
    // (which would fire on every call) on platforms where Stopwatch.Frequency is
    // very low.
    private static long MillisecondsToTicks(int ms)
    {
        if (ms <= 0)
        {
            return 1L;
        }

        var ticks = (Stopwatch.Frequency * ms) / 1000L;
        return ticks < 1L ? 1L : ticks;
    }

    private static void ValidatePollingInterval(int checkEveryMs)
    {
        if (checkEveryMs < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(checkEveryMs), checkEveryMs, "Polling intervals must be non-negative.");
        }
    }
}
