using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using AdaskoTheBeAsT.Interop.Threading;
using Microsoft.Extensions.Hosting;

namespace StaService;

/// <summary>A hosted-service pattern with exclusively owned synchronous components.</summary>
internal sealed class CalculationService(SingleThreadedApartmentTaskScheduler scheduler, bool failStartup = false) : IHostedService, IDisposable
{
    private readonly Channel<CalculationRequest> _requests = Channel.CreateUnbounded<CalculationRequest>();
    private readonly CancellationTokenSource _stopping = new();
    private readonly List<FakeCalculator> _components = [];
    private Task _processing = Task.CompletedTask;
    private int _accepting;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Do not let post-execution cancellation hide successfully created resources.
            await scheduler.RunAsync(
                () =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
#pragma warning disable CC0022 // Components are retained and released in reverse order during cleanup.
                        _components.Add(new FakeCalculator());
#pragma warning restore CC0022
                        if (failStartup)
                        {
                            throw new InvalidOperationException("Deliberate partial startup failure.");
                        }

#pragma warning disable CC0022 // Components are retained and released in reverse order during cleanup.
                        _components.Add(new FakeCalculator());
#pragma warning restore CC0022
                        return 0;
                    }
                    catch
                    {
                        ReleaseComponents();
                        throw;
                    }
                },
                CancellationToken.None).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            _processing = ProcessRequestsAsync(_stopping.Token);
            Volatile.Write(ref _accepting, 1);
        }
        catch
        {
            await CleanupAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async Task<decimal> AddAsync(decimal left, decimal right, CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _accepting) == 0)
        {
            throw new InvalidOperationException("The service is not accepting requests.");
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stopping.Token);
        var request = new CalculationRequest(left, right, linked.Token);
        using var registration = linked.Token.Register(
            static state => ((CalculationRequest)state!).Cancel(),
            request);
        if (!_requests.Writer.TryWrite(request))
        {
            throw new InvalidOperationException("The service is stopping.");
        }

#pragma warning disable VSTHRD003 // Completion is owned by this service's request processor.
        return await request.Completion.Task.ConfigureAwait(false);
#pragma warning restore VSTHRD003
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        Volatile.Write(ref _accepting, 0);
        _requests.Writer.TryComplete();
        await _stopping.CancelAsync().ConfigureAwait(false);
        try
        {
#pragma warning disable VSTHRD003 // Wait for the service's owned request processor.
            await _processing.TimeoutAfterAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
#pragma warning restore VSTHRD003
        }
        finally
        {
            CancelPending();

            // Cleanup remains queued even if the host's shutdown token is already canceled.
            await CleanupAsync().ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        _stopping.Dispose();
    }

    private async Task ProcessRequestsAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var request in _requests.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                await ProcessRequestAsync(request).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException ex) when (stoppingToken.IsCancellationRequested)
        {
            Console.WriteLine($"Request processing stopped during shutdown ({ex.HResult}).");
        }
        finally
        {
            CancelPending();
        }
    }

    private async Task ProcessRequestAsync(CalculationRequest request)
    {
        if (request.Token.IsCancellationRequested)
        {
            request.Cancel();
            return;
        }

        try
        {
            var value = await scheduler.RunCooperativeAsync(
                (yield, token) => _components[0].Add(request.Left, request.Right, yield, token),
                request.Token).ConfigureAwait(false);
            request.Completion.TrySetResult(value);
        }
        catch (OperationCanceledException) when (request.Token.IsCancellationRequested)
        {
            request.Cancel();
        }
        catch (Exception ex)
        {
            request.Completion.TrySetException(ex);
        }
    }

    private Task CleanupAsync()
    {
        var cleanup = scheduler.RunAsync(
            () =>
            {
                ReleaseComponents();
                return 0;
            },
            Timeout.InfiniteTimeSpan,
            CancellationToken.None);
        _ = cleanup.ContinueWith(
            static task => _ = task.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return cleanup.TimeoutAfterAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
    }

    private void ReleaseComponents()
    {
        try
        {
            for (var index = _components.Count - 1; index >= 0; index--)
            {
                // Replace with Executor.Free for handles exclusively owned by this service.
                _components[index].Dispose();
            }
        }
        finally
        {
            _components.Clear();
        }
    }

    private void CancelPending()
    {
        while (_requests.Reader.TryRead(out var request))
        {
            request.Cancel();
        }
    }

    private sealed class CalculationRequest(decimal left, decimal right, CancellationToken token)
    {
        public decimal Left { get; } = left;

        public decimal Right { get; } = right;

        public CancellationToken Token { get; } = token;

        public TaskCompletionSource<decimal> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Cancel()
        {
            Completion.TrySetCanceled(Token);
        }
    }
}
