using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using AdaskoTheBeAsT.Interop.Threading;

namespace Threading.Benchmarks;

internal static class LatencyProbe
{
    public static async Task RunAsync()
    {
        const int count = 1000;
        using var scheduler = new SingleThreadedApartmentTaskScheduler();
        await scheduler.RunAsync(static () => 0, CancellationToken.None).ConfigureAwait(false);
        var queueTicks = new long[count];
        var executionTicks = new long[count];
        var tasks = new Task<int>[count];
        for (var i = 0; i < count; i++)
        {
            var index = i;
            var submitted = Stopwatch.GetTimestamp();
#pragma warning disable AsyncFixer04 // All burst tasks are awaited below before disposing the scheduler.
            tasks[i] = scheduler.RunAsync(
                () =>
                {
                    var started = Stopwatch.GetTimestamp();
                    Thread.SpinWait(100);
                    executionTicks[index] = Stopwatch.GetTimestamp() - started;
                    queueTicks[index] = started - submitted;
                    return index;
                },
                CancellationToken.None);
#pragma warning restore AsyncFixer04
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
        Report("submission-to-start (includes admission)", queueTicks);
        Report("delegate execution (SpinWait(100))", executionTicks);
    }

    private static void Report(string label, long[] ticks)
    {
        Array.Sort(ticks);
        var scale = 1_000_000d / Stopwatch.Frequency;
        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{label}: p50={ticks[ticks.Length / 2] * scale:F3} us, p95={ticks[(int)(ticks.Length * 0.95)] * scale:F3} us"));
    }
}
