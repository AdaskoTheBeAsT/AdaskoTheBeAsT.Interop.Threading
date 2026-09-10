using System;
using System.Threading;

namespace AdaskoTheBeAsT.Interop.Threading;

internal static class TimeoutValidation
{
    public static void Validate(TimeSpan timeout, string parameterName)
    {
        if (timeout != Timeout.InfiniteTimeSpan &&
            (timeout < TimeSpan.Zero || timeout.TotalMilliseconds > int.MaxValue - 1))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                timeout,
                $"Timeout must be {nameof(Timeout.InfiniteTimeSpan)} or between zero and {int.MaxValue - 1} milliseconds.");
        }
    }
}
