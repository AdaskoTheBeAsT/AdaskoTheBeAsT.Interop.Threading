using System;
using System.Threading;
using System.Threading.Tasks;

namespace AdaskoTheBeAsT.Interop.Threading;

internal sealed class StaWorkItem<T> : IStaWorkItem
{
    private const int PendingState = 0;
    private const int ExecutingState = 1;
    private const int CompletedState = 2;
    private const int CanceledState = 3;

    private readonly Func<T?> _work;

    private readonly CancellationToken _cancellationToken;

    private readonly TaskCompletionSource<T?> _taskCompletionSource =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private CancellationTokenRegistration _cancellationRegistration;

    private int _state = PendingState;

    public StaWorkItem(Func<T?> work, CancellationToken cancellationToken)
    {
        _work = work ?? throw new ArgumentNullException(nameof(work));
        _cancellationToken = cancellationToken;

        if (cancellationToken.CanBeCanceled)
        {
            _cancellationRegistration = cancellationToken.Register(
                static state => ((StaWorkItem<T>)state!).OnCanceled(),
                this);
        }
    }

    public Task<T?> Task => _taskCompletionSource.Task;

    public void Execute()
    {
        if (Interlocked.CompareExchange(ref _state, ExecutingState, PendingState) != PendingState)
        {
            _cancellationRegistration.Dispose();
            return;
        }

        try
        {
            var result = _work();
            _taskCompletionSource.TrySetResult(result);
        }
        catch (Exception ex)
        {
            _taskCompletionSource.TrySetException(ex);
        }
        finally
        {
            Interlocked.Exchange(ref _state, CompletedState);
            _cancellationRegistration.Dispose();
        }
    }

    public void Cancel()
    {
        if (Interlocked.CompareExchange(ref _state, CanceledState, PendingState) == PendingState)
        {
            _taskCompletionSource.TrySetCanceled();
        }

        _cancellationRegistration.Dispose();
    }

    private void OnCanceled()
    {
        if (Interlocked.CompareExchange(ref _state, CanceledState, PendingState) == PendingState)
        {
            _taskCompletionSource.TrySetCanceled(_cancellationToken);
        }
    }
}
