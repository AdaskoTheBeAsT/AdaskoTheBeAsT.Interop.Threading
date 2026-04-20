using System;
#if NET8_0_OR_GREATER
using System.Runtime.Versioning;
#endif
using System.Threading;
using System.Threading.Tasks;

namespace AdaskoTheBeAsT.Interop.Threading;

/// <summary>
/// Schedules delegates onto a single reusable background STA thread with an OLE message loop.
/// All queued work items are serialized on the same STA thread for the lifetime of the instance.
/// </summary>
#if NET8_0_OR_GREATER
[SupportedOSPlatform("windows")]
#endif
public interface ISingleThreadedApartmentTaskScheduler : IDisposable
{
    /// <summary>
    /// Queues a delegate onto the STA scheduler and provides a <see cref="StaYield"/> helper for cooperative message pumping.
    /// </summary>
    /// <typeparam name="T">The type returned by the delegate.</typeparam>
    /// <param name="work">The delegate to execute on the STA thread.</param>
    /// <param name="cancellationToken">A token that can cancel the queued operation before it starts.</param>
    /// <returns>A task that completes with the delegate result, faults with the original exception, or is canceled.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="work"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when the scheduler has already been disposed.</exception>
    Task<T?> RunAsync<T>(Func<StaYield, T?> work, CancellationToken cancellationToken = default);

    /// <summary>
    /// Queues an action onto the STA scheduler and provides a <see cref="StaYield"/> helper for cooperative message pumping.
    /// </summary>
    /// <param name="work">The action to execute on the STA thread.</param>
    /// <param name="cancellationToken">A token that can cancel the queued operation before it starts.</param>
    /// <returns>A task that completes when the action finishes, faults with the original exception, or is canceled.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="work"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when the scheduler has already been disposed.</exception>
    Task RunAsync(Action<StaYield> work, CancellationToken cancellationToken = default);

    /// <summary>
    /// Queues a delegate onto the STA scheduler.
    /// </summary>
    /// <typeparam name="T">The type returned by the delegate.</typeparam>
    /// <param name="func">The delegate to execute on the STA thread.</param>
    /// <param name="cancellationToken">A token that can cancel the queued operation before it starts.</param>
    /// <returns>A task that completes with the delegate result, faults with the original exception, or is canceled.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="func"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when the scheduler has already been disposed.</exception>
    Task<T?> RunAsync<T>(Func<T?> func, CancellationToken cancellationToken);

    /// <summary>
    /// Queues a delegate onto the STA scheduler with a per-call timeout.
    /// If the delegate has not produced a result before <paramref name="timeout"/> elapses,
    /// the returned task faults with <see cref="TimeoutException"/>.
    /// </summary>
    /// <typeparam name="T">The type returned by the delegate.</typeparam>
    /// <param name="func">The delegate to execute on the STA thread.</param>
    /// <param name="timeout">The maximum amount of time to wait for the delegate to complete.
    /// Pass <see cref="Timeout.InfiniteTimeSpan"/> to wait indefinitely.</param>
    /// <param name="cancellationToken">A token that can cancel the queued operation.</param>
    /// <returns>A task that completes with the delegate result, times out, faults with the original exception, or is canceled.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="func"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when the scheduler has already been disposed.</exception>
    /// <exception cref="TimeoutException">Thrown from the returned task when <paramref name="timeout"/> elapses first.</exception>
    Task<T?> RunAsync<T>(Func<T?> func, TimeSpan timeout, CancellationToken cancellationToken);

    /// <summary>
    /// Stops the scheduler from accepting new work and signals the background STA thread to shut down.
    /// Pending queued items are canceled.
    /// </summary>
    void Shutdown();
}
