using System;
using System.Threading;
using System.Threading.Tasks;
#if NET8_0_OR_GREATER
using System.Runtime.Versioning;
#endif

namespace AdaskoTheBeAsT.Interop.Threading;

/// <summary>
/// Runs delegates on a dedicated background STA thread.
/// Each call creates a new thread, executes the supplied delegate there, and pumps any remaining messages before the thread exits.
/// </summary>
#if NET8_0_OR_GREATER
[SupportedOSPlatform("windows")]
#endif
public static class SingleThreadedApartmentTask
{
    /// <summary>
    /// Runs a delegate on a dedicated STA thread and provides a <see cref="StaYield"/> helper for cooperative message pumping.
    /// </summary>
    /// <typeparam name="T">The type returned by the delegate.</typeparam>
    /// <param name="func">The delegate to execute on the STA thread.</param>
    /// <param name="cancellationToken">A token that can cancel the operation before or during execution.</param>
    /// <returns>A task that completes with the delegate result, faults with the original exception, or is canceled.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="func"/> is <see langword="null"/>.</exception>
    public static Task<T> RunAsync<T>(
        Func<StaYield, T> func,
        CancellationToken cancellationToken)
    {
        if (func == null)
        {
            throw new ArgumentNullException(nameof(func));
        }

        return RunAsync(() => func(new StaYield()), cancellationToken);
    }

    // ReSharper disable once InconsistentNaming
    // ReSharper disable once MemberCanBePrivate.Global

    /// <summary>
    /// Runs a delegate on a dedicated STA thread.
    /// </summary>
    /// <typeparam name="T">The type returned by the delegate.</typeparam>
    /// <param name="func">The delegate to execute on the STA thread.</param>
    /// <param name="cancellationToken">A token that can cancel the operation before or during execution.</param>
    /// <returns>A task that completes with the delegate result, faults with the original exception, or is canceled.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="func"/> is <see langword="null"/>.</exception>
    public static Task<T> RunAsync<T>(
        Func<T> func,
        CancellationToken cancellationToken)
    {
        if (func == null)
        {
            throw new ArgumentNullException(nameof(func));
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<T>(cancellationToken);
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
            Name = "STA Task Thread",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }

    /// <summary>
    /// Runs a delegate on a dedicated STA thread and applies a timeout to the resulting task.
    /// </summary>
    /// <typeparam name="T">The type returned by the delegate.</typeparam>
    /// <param name="timeSpan">The maximum amount of time to wait for the delegate to complete.</param>
    /// <param name="func">The delegate to execute on the STA thread.</param>
    /// <param name="cancellationToken">A token that can cancel the operation before the timeout expires.</param>
    /// <returns>A task that completes with the delegate result, faults with the original exception, times out, or is canceled.</returns>
    public static Task<T> RunWithTimeoutAsync<T>(
        TimeSpan timeSpan,
        Func<T> func,
        CancellationToken cancellationToken) =>
        RunAsync(func, cancellationToken)
            .TimeoutAfterAsync(timeSpan, cancellationToken);
}
