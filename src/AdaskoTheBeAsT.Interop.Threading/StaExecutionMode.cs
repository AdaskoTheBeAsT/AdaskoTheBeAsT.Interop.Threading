namespace AdaskoTheBeAsT.Interop.Threading;

/// <summary>
/// Specifies how synchronous <see cref="SingleThreadedApartmentTask"/> operations acquire an STA thread.
/// </summary>
public enum StaExecutionMode
{
    /// <summary>
    /// Always runs work on a dedicated background STA thread with an isolated message queue,
    /// bounded final message pump, and balanced COM/OLE teardown.
    /// </summary>
    DedicatedThread = 0,

    /// <summary>
    /// Executes inline on the calling thread via <see cref="StaApartment.EnterScope(bool)"/> when the caller
    /// already runs on a managed STA thread (leaving the caller's message queue intact on exit),
    /// or falls back to <see cref="DedicatedThread"/> when called from an MTA or uninitialized thread.
    /// </summary>
    ReuseCurrentStaThread = 1,
}
