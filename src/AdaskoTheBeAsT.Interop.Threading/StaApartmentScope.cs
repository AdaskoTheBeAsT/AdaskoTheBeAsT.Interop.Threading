using System;
using System.Runtime.ExceptionServices;
#if NET8_0_OR_GREATER
using System.Runtime.Versioning;
#endif
using System.Threading;

namespace AdaskoTheBeAsT.Interop.Threading;

/// <summary>
/// Represents a balanced COM (and optional OLE) single-threaded apartment initialization lease bound to the thread that entered it.
/// </summary>
#if NET8_0_OR_GREATER
[SupportedOSPlatform("windows")]
#endif
public sealed class StaApartmentScope : IDisposable
{
    private readonly StaPlatform _platform;
    private readonly bool _comInitialized;
    private readonly bool _oleInitialized;
    private int _disposed;

    internal StaApartmentScope(
        StaPlatform platform,
        ThreadAffinity affinity,
        bool comInitialized,
        bool oleInitialized)
    {
        _platform = platform;
        Affinity = affinity;
        _comInitialized = comInitialized;
        _oleInitialized = oleInitialized;
    }

    /// <summary>
    /// Gets the thread-affinity guard captured when the scope was entered.
    /// </summary>
    public ThreadAffinity Affinity { get; }

    /// <summary>
    /// Releases the owned OLE and COM STA apartment reference counts on the thread that entered the scope.
    /// Calling <see cref="Dispose"/> multiple times on the owning thread is a safe no-op; calling it from
    /// another thread throws <see cref="InvalidOperationException"/> without altering that thread's COM state.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when called from a thread other than <see cref="Affinity"/>.</exception>
    public void Dispose()
    {
        Affinity.ThrowIfWrongThread(nameof(StaApartmentScope));
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Exception? failure = null;
        if (_oleInitialized)
        {
            failure = StaPlatform.InvokeCleanupStep(_platform.Uninitialize, failure);
        }

        if (_comInitialized)
        {
            failure = StaPlatform.InvokeCleanupStep(_platform.UninitializeCom, failure);
        }

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
