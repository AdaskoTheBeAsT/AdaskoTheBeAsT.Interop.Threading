using System.Threading;
using System.Threading.Tasks;
using AdaskoTheBeAsT.Interop.Threading;
using AdaskoTheBeAsT.Interop.Threading.Test;
using BenchmarkDotNet.Attributes;

namespace Threading.Benchmarks;

// BenchmarkDotNet owns these resources through GlobalSetup and GlobalCleanup.
#pragma warning disable CA1001, IDISP006, IDISP003
[MemoryDiagnoser]
public class MessageBenchmarks
{
    // Both resources are released by BenchmarkDotNet's owning-thread cleanup.
#pragma warning disable CA2213, IDISP002
    private SingleThreadedApartmentTaskScheduler _scheduler = null!;
    private MessageWindow _window = null!;
#pragma warning restore CA2213, IDISP002

    [GlobalSetup]
    public async Task SetupAsync()
    {
        _scheduler = new SingleThreadedApartmentTaskScheduler();
        _window = (await _scheduler.RunAsync(
            static () =>
            {
                var window = new MessageWindow { Repost = true };
                window.Post();
                return window;
            },
            CancellationToken.None).ConfigureAwait(false))!;
    }

    [GlobalCleanup]
    public async Task CleanupAsync()
    {
        await _scheduler.RunAsync(
            () =>
            {
                _window.Repost = false;
                _window.Dispose();
                return 0;
            },
            CancellationToken.None).ConfigureAwait(false);
        _scheduler.Dispose();
    }

    [Benchmark]
    public Task<int> QueuedWorkUnderMessageTrafficAsync()
    {
        return _scheduler.RunAsync(() => _window.Received, CancellationToken.None);
    }
}
