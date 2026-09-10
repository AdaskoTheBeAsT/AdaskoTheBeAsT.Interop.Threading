using System;
#if NET8_0_OR_GREATER
using System.Runtime.Versioning;
#endif
using System.Security.AccessControl;

namespace AdaskoTheBeAsT.Interop.Threading;

/// <summary>Explicit policies for synchronous, thread-affine named mutex execution.</summary>
#if NET8_0_OR_GREATER
[SupportedOSPlatform("windows")]
#endif
public sealed class MutexExecutionOptions
{
    /// <summary>Gets or sets a value indicating whether to use the machine-global namespace instead of the current session.</summary>
    public bool IsGlobal { get; set; }

    /// <summary>Gets or sets the acquisition budget. Defaults to an infinite wait.</summary>
    public TimeSpan Timeout { get; set; } = System.Threading.Timeout.InfiniteTimeSpan;

    /// <summary>
    /// Gets or sets the creation ACL. Null grants synchronization and release rights to the current user only.
    /// Existing ACLs are never changed. Supply an explicit ACL for cross-user sharing.
    /// </summary>
    public MutexSecurity? Security { get; set; }

    /// <summary>Gets or sets a value indicating whether abandonment throws before executing user code. Defaults to true.</summary>
    public bool FailOnAbandonedMutex { get; set; } = true;
}
