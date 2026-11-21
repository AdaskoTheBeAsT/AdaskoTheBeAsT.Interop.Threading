using System;
#if NET8_0_OR_GREATER
using System.Runtime.Versioning;
#endif
using System.Threading;
using System.Threading.Tasks;

namespace AdaskoTheBeAsT.Interop.Threading;

/// <summary>
/// Provides ergonomic <see cref="Action"/> extension overloads for <see cref="ISingleThreadedApartmentTaskScheduler"/>
/// without adding required members to external interface implementations.
/// </summary>
#if NET8_0_OR_GREATER
[SupportedOSPlatform("windows")]
#endif
public static class SingleThreadedApartmentTaskSchedulerExtensions
{
    /// <summary>
    /// Queues a synchronous action onto the STA scheduler.
    /// </summary>
    /// <param name="scheduler">The target STA scheduler.</param>
    /// <param name="action">The synchronous action to execute on the STA thread.</param>
    /// <param name="cancellationToken">A token that can cancel the queued operation before it starts.</param>
    /// <returns>A task that completes when the action finishes, faults with the original exception, or is canceled.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="scheduler"/> or <paramref name="action"/> is <see langword="null"/>.</exception>
    public static Task RunAsync(
        this ISingleThreadedApartmentTaskScheduler scheduler,
        Action action,
        CancellationToken cancellationToken = default)
    {
        ValidateArguments(scheduler, action);
#pragma warning disable CC0031 // Validated by ValidateArguments.
        return scheduler.RunAsync<object?>(
            () =>
            {
                action();
                return null;
            },
            cancellationToken);
#pragma warning restore CC0031
    }

    /// <summary>
    /// Queues a synchronous action onto the STA scheduler with an explicit timeout.
    /// </summary>
    /// <param name="scheduler">The target STA scheduler.</param>
    /// <param name="action">The synchronous action to execute on the STA thread.</param>
    /// <param name="timeout">The maximum amount of time to wait for the action to complete, or <see cref="Timeout.InfiniteTimeSpan"/>.</param>
    /// <param name="cancellationToken">A token that can cancel the queued operation or wait.</param>
    /// <returns>A task that completes when the action finishes, times out, faults with the original exception, or is canceled.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="scheduler"/> or <paramref name="action"/> is <see langword="null"/>.</exception>
    public static Task RunAsync(
        this ISingleThreadedApartmentTaskScheduler scheduler,
        Action action,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ValidateArguments(scheduler, action);
#pragma warning disable CC0031 // Validated by ValidateArguments.
        return scheduler.RunAsync<object?>(
            () =>
            {
                action();
                return null;
            },
            timeout,
            cancellationToken);
#pragma warning restore CC0031
    }

    private static void ValidateArguments(ISingleThreadedApartmentTaskScheduler scheduler, Action action)
    {
#if NET8_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(action);
#else
        if (scheduler is null)
        {
            throw new ArgumentNullException(nameof(scheduler));
        }

        if (action is null)
        {
            throw new ArgumentNullException(nameof(action));
        }
#endif
    }
}
