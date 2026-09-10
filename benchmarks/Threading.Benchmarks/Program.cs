using System;
using System.Threading.Tasks;
using BenchmarkDotNet.Running;

namespace Threading.Benchmarks;

internal static class Program
{
    private static async Task Main(string[] args)
    {
        if (args.Length == 1 && string.Equals(args[0], "--latency", StringComparison.Ordinal))
        {
            await LatencyProbe.RunAsync().ConfigureAwait(false);
            return;
        }

        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
    }
}
