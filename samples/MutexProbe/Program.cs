using System;
using System.Threading;
using AdaskoTheBeAsT.Interop.Threading;

namespace MutexProbe;

internal static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length != 4)
        {
            return 1;
        }

        using var cancellation = new CancellationTokenSource();
        var mode = args[0];
        var options = new MutexExecutionOptions { Timeout = TimeSpan.FromMilliseconds(250) };
        if (string.Equals(mode, "cancel", StringComparison.Ordinal))
        {
            options.Timeout = Timeout.InfiniteTimeSpan;
            cancellation.CancelAfter(100);
        }

        try
        {
            if (string.Equals(mode, "abandon", StringComparison.Ordinal))
            {
                using var mutex = new Mutex(initiallyOwned: false, args[1]);
                if (!mutex.WaitOne(TimeSpan.FromSeconds(5)))
                {
                    return 1;
                }

                // Intentional test-only abandonment. A parent handle keeps the kernel object alive.
                SignalAndWait(args[2], args[3]);
                return 0;
            }

            return MutexHelper.RunInMutex(
                args[1],
                options,
                () =>
                {
                    if (string.Equals(mode, "hold", StringComparison.Ordinal))
                    {
                        SignalAndWait(args[2], args[3]);
                    }

                    return 0;
                },
                cancellation.Token);
        }
        catch (TimeoutException)
        {
            return 2;
        }
        catch (AbandonedMutexException)
        {
            return 3;
        }
        catch (OperationCanceledException)
        {
            return 4;
        }
    }

    private static void SignalAndWait(string readyName, string releaseName)
    {
        using var ready = EventWaitHandle.OpenExisting(readyName);
        using var release = EventWaitHandle.OpenExisting(releaseName);
        ready.Set();
        if (!release.WaitOne(TimeSpan.FromSeconds(10)))
        {
            throw new TimeoutException("The parent test harness did not release the probe.");
        }
    }
}
