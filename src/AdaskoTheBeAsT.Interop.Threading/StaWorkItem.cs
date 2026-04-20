using System;
#if NET8_0_OR_GREATER
using System.Runtime.Versioning;
#endif
using System.Threading;
using System.Threading.Tasks;

namespace AdaskoTheBeAsT.Interop.Threading;

#if NET8_0_OR_GREATER
[SupportedOSPlatform("windows")]
#endif
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

    private readonly CancellationTokenRegistration _cancellationRegistration;

    private int _state = PendingState;

    public StaWorkItem(Func<T?> work, CancellationToken cancellationToken)
    {
        _work = work ?? throw new ArgumentNullException(nameof(work));
        _cancellationToken = cancellationToken;

        _cancellationRegistration = cancellationToken.CanBeCanceled
            ? cancellationToken.Register(
                static state => ((StaWorkItem<T>)state!).OnCanceled(),
                this)
            : default;
    }

    public Task<T?> Task => _taskCompletionSource.Task;

    public void Execute()
    {
        if (Interlocked.CompareExchange(ref _state, ExecutingState, PendingState) != PendingState)
        {
            _cancellationRegistration.Dispose();
            return;
        }

        // Re-check just before invoking the delegate: the token could have been canceled
        // between dequeue and here; bail out early rather than running user code.
        if (_cancellationToken.IsCancellationRequested)
        {
            _taskCompletionSource.TrySetCanceled(_cancellationToken);
            Interlocked.Exchange(ref _state, CanceledState);
            _cancellationRegistration.Dispose();
            return;
        }

        try
        {
            var result = _work();

            // If cancellation was requested while the work was running, surface it
            // as a cancellation rather than a successful result (cooperative model:
            // only kicks in if the user code itself did not throw OperationCanceledException).
            if (_cancellationToken.IsCancellationRequested)
            {
                _taskCompletionSource.TrySetCanceled(_cancellationToken);
            }
            else
            {
                _taskCompletionSource.TrySetResult(result);
            }
        }
        catch (OperationCanceledException oce) when (oce.CancellationToken == _cancellationToken
                                                     || _cancellationToken.IsCancellationRequested)
        {
            _taskCompletionSource.TrySetCanceled(_cancellationToken);
        }
        catch (Exception ex)
        {
            _taskCompletionSource.TrySetException(ex);
        }
        finally
        {
            Interlocked.CompareExchange(ref _state, CompletedState, ExecutingState);
            _cancellationRegistration.Dispose();
        }
    }

    public void Cancel()
    {
        if (Interlocked.CompareExchange(ref _state, CanceledState, PendingState) == PendingState)
        {
            // Preserve the associated cancellation token so callers observing
            // the resulting TaskCanceledException see the same token here and
            // from OnCanceled()/Execute(), giving consistent cancellation diagnostics.
            _taskCompletionSource.TrySetCanceled(_cancellationToken);
        }

        _cancellationRegistration.Dispose();
    }

    private void OnCanceled()
    {
        // Only cancel if the work has not yet started executing. Once it is running,
        // the work item cooperatively observes the token via _cancellationToken.IsCancellationRequested.
        if (Interlocked.CompareExchange(ref _state, CanceledState, PendingState) == PendingState)
        {
            _taskCompletionSource.TrySetCanceled(_cancellationToken);
        }
    }
}
