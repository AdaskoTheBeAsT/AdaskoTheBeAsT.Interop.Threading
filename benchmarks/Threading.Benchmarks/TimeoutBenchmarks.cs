using System;
using System.Threading;
using System.Threading.Tasks;
using AdaskoTheBeAsT.Interop.Threading;
using BenchmarkDotNet.Attributes;

namespace Threading.Benchmarks;

[MemoryDiagnoser]
public class TimeoutBenchmarks
{
    private readonly Task<int> _completed = Task.FromResult(42);
    private readonly TaskCompletionSource<int> _pending = new(TaskCreationOptions.RunContinuationsAsynchronously);

    [Benchmark(Baseline = true)]
    public Task<int> CompletedAsync()
    {
        return _completed.TimeoutAfterAsync(TimeSpan.FromSeconds(1), CancellationToken.None);
    }

    [Benchmark]
    public async Task<bool> ExpiredAsync()
    {
        try
        {
#pragma warning disable VSTHRD003 // The benchmark intentionally waits on a never-completing source.
            await _pending.Task.TimeoutAfterAsync(TimeSpan.Zero, CancellationToken.None).ConfigureAwait(false);
#pragma warning restore VSTHRD003
        }
        catch (TimeoutException)
        {
            // Exception construction is part of the timed-out wait's cost.
            return true;
        }

        return false;
    }
}
