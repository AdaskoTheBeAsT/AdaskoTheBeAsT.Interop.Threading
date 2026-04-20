using System;
using System.Threading;

namespace AdaskoTheBeAsT.Interop.Threading;

/// <summary>
/// Configuration options for <see cref="SingleThreadedApartmentTaskScheduler"/>.
/// </summary>
public sealed class SingleThreadedApartmentTaskSchedulerOptions
{
    /// <summary>
    /// The default name used for the background STA thread when no other value is supplied.
    /// </summary>
    public const string DefaultThreadName = "STA Task Scheduler Thread";

    /// <summary>
    /// Gets or sets the name to assign to the background STA thread.
    /// Defaults to <see cref="DefaultThreadName"/>. Useful for diagnostics and profiling.
    /// </summary>
    public string ThreadName { get; set; } = DefaultThreadName;

    /// <summary>
    /// Gets or sets the default per-work-item timeout applied when no explicit timeout is passed
    /// to <see cref="ISingleThreadedApartmentTaskScheduler.RunAsync{T}(System.Func{T},System.TimeSpan,System.Threading.CancellationToken)"/>.
    /// Use <see cref="Timeout.InfiniteTimeSpan"/> (the default) to disable the default timeout.
    /// Individual calls may override this value.
    /// </summary>
    public TimeSpan DefaultWorkItemTimeout { get; set; } = Timeout.InfiniteTimeSpan;
}
