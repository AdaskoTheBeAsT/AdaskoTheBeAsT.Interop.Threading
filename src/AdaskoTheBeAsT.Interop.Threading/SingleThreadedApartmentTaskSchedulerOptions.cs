using System;
using System.Globalization;
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

    /// <summary>
    /// Gets or sets the maximum number of pending (not executing) items.
    /// Null means unlimited. A full queue rejects new work rather than blocking its caller.
    /// </summary>
    public int? MaximumPendingWorkItems { get; set; }

    /// <summary>Gets or sets a value indicating whether payload-free EventSource diagnostics are enabled.</summary>
    public bool EnableDiagnostics { get; set; }

    /// <summary>
    /// Gets or sets an optional callback invoked synchronously on the constructing thread before starting
    /// the background STA thread. Use this to prepare process-level STA shims (such as Nano Server COM shims)
    /// before runtime thread startup initializes COM.
    /// </summary>
    public Action? BeforeThreadStart { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the worker verifies that its native COM apartment is STA
    /// after OLE initialization and recovers from an uninitialized apartment via <c>CoInitializeEx(COINIT_APARTMENTTHREADED)</c>.
    /// Defaults to <see langword="true"/>.
    /// </summary>
    public bool VerifyNativeStaApartment { get; set; } = true;

    /// <summary>
    /// Gets or sets an optional factory invoked once on the owned STA thread during startup before queued work is admitted.
    /// When non-<see langword="null"/>, the returned <see cref="IDisposable"/> is disposed once on the same STA thread during shutdown
    /// before native library cleanup and OLE uninitialization.
    /// </summary>
    public Func<IDisposable?>? CreateThreadScope { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of pending Windows messages to pump non-blockingly after each completed work item,
    /// preserving <c>WM_QUIT</c> for the main scheduler loop. Defaults to <c>0</c> (disabled).
    /// When positive, also runs one bounded final message-pump pass during worker teardown.
    /// </summary>
    public int PostWorkItemMessagePumpBatchSize { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the worker calls <c>CoFreeUnusedLibrariesEx(0, 0)</c> followed by one bounded
    /// message-pump pass on the owned STA thread after disposing the thread scope and before OLE uninitialization.
    /// Defaults to <see langword="false"/>.
    /// </summary>
    public bool FreeUnusedComLibrariesOnShutdown { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the worker clears pending x87/SSE floating-point exceptions and restores
    /// masked exception flags (<c>_MCW_EM</c>) without altering rounding or precision modes on 32-bit Windows (<c>win-x86</c>)
    /// after thread-scope creation and after each work item. Defaults to <see langword="false"/>.
    /// </summary>
    public bool ResetFloatingPointMaskAfterWorkItem { get; set; }

    /// <summary>
    /// Gets or sets an optional <see cref="CultureInfo"/> applied to <see cref="CultureInfo.CurrentCulture"/>
    /// on the owned STA thread before <see cref="CreateThreadScope"/> and queued work items execute.
    /// </summary>
    public CultureInfo? ThreadCulture { get; set; }

    /// <summary>
    /// Gets or sets an optional <see cref="CultureInfo"/> applied to <see cref="CultureInfo.CurrentUICulture"/>
    /// on the owned STA thread before <see cref="CreateThreadScope"/> and queued work items execute.
    /// </summary>
    public CultureInfo? ThreadUICulture { get; set; }

    /// <summary>
    /// Gets or sets an optional positive Win32 LCID (for example <c>0x0409</c> for <c>en-US</c> or <c>0x0809</c> for <c>en-GB</c>)
    /// applied via <c>kernel32!SetThreadLocale</c> on the owned STA thread before <see cref="CreateThreadScope"/> and queued work items execute.
    /// </summary>
    public int? WindowsThreadLocaleId { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the worker verifies and restores its baseline
    /// <see cref="CultureInfo.CurrentCulture"/>, <see cref="CultureInfo.CurrentUICulture"/>, and Win32 thread locale
    /// after <see cref="CreateThreadScope"/> and after each executed work item. Defaults to <see langword="false"/>.
    /// </summary>
    public bool ResetThreadCultureAndLocaleAfterWorkItem { get; set; }

    /// <summary>
    /// Gets or sets the maximum stack size, in bytes, for the owned background STA thread, or <c>0</c> (default)
    /// to use the default stack size from the executable header / runtime.
    /// </summary>
    public int MaxStackSize { get; set; }
}
