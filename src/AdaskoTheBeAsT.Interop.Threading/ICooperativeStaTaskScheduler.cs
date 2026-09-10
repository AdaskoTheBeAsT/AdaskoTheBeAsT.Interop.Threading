using System;
#if NET8_0_OR_GREATER
using System.Runtime.Versioning;
#endif
using System.Threading;
using System.Threading.Tasks;

namespace AdaskoTheBeAsT.Interop.Threading;

/// <summary>Optional capabilities, without adding required members to existing scheduler implementations.</summary>
#if NET8_0_OR_GREATER
[SupportedOSPlatform("windows")]
#endif
public interface ICooperativeStaTaskScheduler : ISingleThreadedApartmentTaskScheduler
{
    /// <summary>Gets a task that completes after the worker exits, or faults with its terminal failure.</summary>
    Task Completion { get; }

    /// <summary>Schedules synchronous work with the effective caller, timeout, and shutdown token.</summary>
    /// <typeparam name="T">Result type.</typeparam>
    /// <param name="work">Synchronous delegate. Async delegates do not retain STA affinity.</param>
    /// <param name="timeout">Queue plus execution wait budget.</param>
    /// <param name="cancellationToken">Caller cancellation.</param>
    /// <returns>The result, cancellation, or failure. Timeout never terminates the worker.</returns>
    Task<T?> RunCooperativeAsync<T>(Func<StaYield, CancellationToken, T?> work, TimeSpan timeout, CancellationToken cancellationToken = default);

    /// <summary>Schedules synchronous work with the effective cancellation token.</summary>
    /// <param name="work">Synchronous action, never async void.</param>
    /// <param name="timeout">Queue plus execution wait budget.</param>
    /// <param name="cancellationToken">Caller cancellation.</param>
    /// <returns>The operation's outcome.</returns>
    Task RunCooperativeAsync(Action<StaYield, CancellationToken> work, TimeSpan timeout, CancellationToken cancellationToken = default);

    /// <summary>Requests shutdown and bounds only the wait for worker termination.</summary>
    /// <param name="timeout">Termination wait budget.</param>
    /// <param name="cancellationToken">Cancels only the termination wait.</param>
    /// <returns>A task representing the bounded wait.</returns>
    Task ShutdownAsync(TimeSpan timeout, CancellationToken cancellationToken = default);
}
