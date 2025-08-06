using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace AdaskoTheBeAsT.Interop.Threading;

/// <summary>
/// http://stackoverflow.com/questions/16720496/set-apartmentstate-on-a-task.
/// </summary>
public static class SingleThreadedApartmentTask
{
    // ReSharper disable once InconsistentNaming
    // ReSharper disable once MemberCanBePrivate.Global
    public static Task<T> RunAsync<T>(
        Func<T> func,
        CancellationToken cancellationToken)
    {
        if (func == null)
        {
            throw new ArgumentNullException(nameof(func));
        }

        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                // If caller already cancelled, bail early
                cancellationToken.ThrowIfCancellationRequested();

                var result = func();
                tcs.TrySetResult(result);
            }
            catch (OperationCanceledException oce)
            {
                // Propagate cooperative cancellation
                tcs.TrySetCanceled(oce.CancellationToken);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
            finally
            {
                // pump any remaining COM messages
                NativeMethods.PumpPendingMessages();
            }
        })
        {
            // won't block process shutdown
            IsBackground = true,
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }

    public static Task<T> RunWithTimeoutAsync<T>(
        TimeSpan timeSpan,
        Func<T> func,
        CancellationToken cancellationToken) =>
        RunAsync(func, cancellationToken)
            .TimeoutAfterAsync(timeSpan, cancellationToken);
}
