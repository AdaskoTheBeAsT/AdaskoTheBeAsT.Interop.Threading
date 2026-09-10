using System.Diagnostics.Tracing;

namespace AdaskoTheBeAsT.Interop.Threading;

/// <summary>Payload-free diagnostics. Opt in through scheduler options and an EventListener.</summary>
[EventSource(Name = "AdaskoTheBeAsT-Interop-Threading")]
internal sealed class StaSchedulerEventSource : EventSource
{
    public static readonly StaSchedulerEventSource Log = new();

    [Event(1, Level = EventLevel.Informational)]
    public void WorkState(int schedulerId, long workId, string state, int pending, double elapsedMilliseconds)
    {
        if (IsEnabled())
        {
            WriteEvent(1, schedulerId, workId, state, pending, elapsedMilliseconds);
        }
    }

    [Event(2, Level = EventLevel.Informational)]
    public void WorkerStopped(int schedulerId, int hresult, int quitCode)
    {
        if (IsEnabled())
        {
            WriteEvent(2, schedulerId, hresult, quitCode);
        }
    }
}
