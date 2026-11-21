using System;
using System.Globalization;

namespace AdaskoTheBeAsT.Interop.Threading;

/// <summary>
/// Configuration options for one-off STA invocations on <see cref="SingleThreadedApartmentTask"/>.
/// </summary>
public sealed class SingleThreadedApartmentTaskOptions
{
    /// <summary>
    /// The default name assigned to a dedicated background STA thread when no custom name is supplied.
    /// </summary>
    public const string DefaultThreadName = "STA Task Thread";

    /// <summary>
    /// Gets or sets the name assigned to a dedicated background STA thread.
    /// Defaults to <see cref="DefaultThreadName"/>.
    /// </summary>
    public string ThreadName { get; set; } = DefaultThreadName;

    /// <summary>
    /// Gets or sets the maximum stack size, in bytes, for a dedicated STA thread, or <c>0</c> (default)
    /// to use the default stack size from the executable header / runtime.
    /// </summary>
    public int MaxStackSize { get; set; }

    /// <summary>
    /// Gets or sets how synchronous <c>SingleThreadedApartmentTask.Run</c> invocations acquire an STA thread.
    /// Defaults to <see cref="StaExecutionMode.DedicatedThread"/>. Asynchronous <c>RunAsync</c> calls always use
    /// a dedicated background STA thread.
    /// </summary>
    public StaExecutionMode ExecutionMode { get; set; } = StaExecutionMode.DedicatedThread;

    /// <summary>
    /// Gets or sets an optional callback invoked synchronously on the calling thread before starting or entering
    /// the STA apartment (for example <c>NanoServerOptions.RefreshStaState</c>).
    /// </summary>
    public Action? BeforeThreadStart { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether native COM STA apartment membership is verified via
    /// <c>CoGetApartmentType</c> after <c>OleInitialize</c>, recovering <c>CO_E_NOTINITIALIZED</c> via
    /// balanced <c>CoInitializeEx(COINIT_APARTMENTTHREADED)</c>. Defaults to <see langword="true"/>.
    /// </summary>
    public bool VerifyNativeStaApartment { get; set; } = true;

    /// <summary>
    /// Gets or sets an optional <see cref="CultureInfo"/> applied to <see cref="CultureInfo.CurrentCulture"/>
    /// for the duration of the STA invocation.
    /// </summary>
    public CultureInfo? ThreadCulture { get; set; }

    /// <summary>
    /// Gets or sets an optional <see cref="CultureInfo"/> applied to <see cref="CultureInfo.CurrentUICulture"/>
    /// for the duration of the STA invocation.
    /// </summary>
    public CultureInfo? ThreadUICulture { get; set; }

    /// <summary>
    /// Gets or sets an optional positive Win32 LCID (for example <c>0x0409</c> or <c>0x0809</c>) applied via
    /// <c>kernel32!SetThreadLocale</c> for the duration of the STA invocation.
    /// </summary>
    public int? WindowsThreadLocaleId { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether pending x87/SSE floating-point status flags are cleared (<c>_clearfp</c>)
    /// and standard exception masks (<c>_MCW_EM</c> via <c>__control87_2</c>) are restored on 32-bit Windows (<c>win-x86</c>)
    /// before exiting the STA invocation. Defaults to <see langword="false"/>.
    /// </summary>
    public bool ResetFloatingPointMaskBeforeExit { get; set; }
}
