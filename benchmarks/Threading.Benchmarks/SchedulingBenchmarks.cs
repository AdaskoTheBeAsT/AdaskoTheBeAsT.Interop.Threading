using System.Threading;
using System.Threading.Tasks;
using AdaskoTheBeAsT.Interop.Threading;
using BenchmarkDotNet.Attributes;

namespace Threading.Benchmarks;

// BenchmarkDotNet calls GlobalSetup once and GlobalCleanup after the measurements.
#pragma warning disable CA1001, IDISP006, IDISP003
[MemoryDiagnoser]
[ThreadingDiagnoser]
public class SchedulingBenchmarks
{
    // BenchmarkDotNet owns the setup/cleanup lifecycle.
#pragma warning disable CA2213, IDISP002
    private SingleThreadedApartmentTaskScheduler _scheduler = null!;
#pragma warning restore CA2213, IDISP002

    [GlobalSetup]
    public void Setup()
    {
        _scheduler = new SingleThreadedApartmentTaskScheduler();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _scheduler.Dispose();
    }

    [Benchmark(Baseline = true)]
    public Task<int> ReusedStaAsync()
    {
        return _scheduler.RunAsync(static () => 42, CancellationToken.None);
    }

    [Benchmark]
#pragma warning disable CC0091 // Benchmark methods must be instance methods.
    public Task<int> OneOffStaAsync()
#pragma warning restore CC0091
    {
        return SingleThreadedApartmentTask.RunAsync(static () => 42, CancellationToken.None);
    }

    [Benchmark(OperationsPerInvoke = 32)]
    public Task<int[]> QueueBurstAsync()
    {
        var tasks = new Task<int>[32];
        for (var i = 0; i < tasks.Length; i++)
        {
            tasks[i] = _scheduler.RunAsync(static () => 42, CancellationToken.None);
        }

        return Task.WhenAll(tasks);
    }

    [Benchmark]
    public Task<bool> PreCanceledAdmissionAsync()
    {
        // WhenAny measures completion without throwing the expected cancellation.
        var canceled = _scheduler.RunAsync(static () => 42, new CancellationToken(canceled: true));
        return IsCanceledAsync(canceled);
    }

    private static async Task<bool> IsCanceledAsync(Task task)
    {
#pragma warning disable VSTHRD003 // Deliberately observe the scheduler's terminal task status.
        await Task.WhenAny(task).ConfigureAwait(false);
#pragma warning restore VSTHRD003
        return task.IsCanceled;
    }
}
