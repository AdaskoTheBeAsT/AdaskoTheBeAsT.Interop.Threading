# ADR 0002: STA lifetime, cancellation, timeouts, and quit messages

Status: accepted for this implementation, 2026-09-09.

## Decisions

All STA and mutex delegates are **synchronous**. An async lambda may create a nested
task or `async void`; awaiting or unwrapping it does not preserve STA affinity.
Pumping may invoke reentrant callbacks inside a delegate. `StaYield` belongs to
the executing thread and does not make arbitrary locks or COM objects reentrancy-safe.

There are three different operations:

1. Stop waiting for a result.
2. Cancel work that has not started.
3. Request that running work stop cooperatively.

`RunCooperativeAsync` supplies a linked caller/timeout/shutdown token. Existing
delegates retain their signatures and are checked before and after execution.
No in-process API terminates a blocked COM call or starts a replacement worker
while the original worker is alive. Hard termination requires process isolation.

Finite budgets are zero through `int.MaxValue - 1` milliseconds; the only negative
sentinel is `Timeout.InfiniteTimeSpan`. Scheduler budgets start at admission and
include queue time. Scheduling validation throws synchronously, before admission.
`TimeoutAfterAsync` preserves its asynchronous validation timing, checking the
source task before the timeout. Completed sources still undergo validation.

## Outcome and token precedence

| State | Outcome |
| --- | --- |
| Scheduling with a pre-canceled caller token | Canceled task with the caller token; delegate not invoked, even after disposal |
| Completed source passed to `TimeoutAfterAsync` | Original task wins over a pre-canceled wait or zero budget |
| Pending item cancellation | Canceled with its effective linked token; removed from the queue; captures released |
| Running work returns after effective token cancellation | Canceled with its effective token |
| Delegate throws matching-token OCE, or OCE while effective token is canceled | Canceled with the effective token |
| Delegate throws unrelated OCE with uncanceled effective token | **Faulted**, preserving the original exception |
| Delegate throws another exception | Faulted with the original exception, even if cancellation was requested |
| Finite wait wins before the item completes | Caller cancellation wins over timeout when observed; otherwise faulted with `TimeoutException` |
| Source wins `WhenAny` | Preserve the source's result, fault/cancellation status, and exception/token |
| Normal shutdown or owned-loop `WM_QUIT` | Reject admission, cancel pending work, request running-work cancellation |
| Worker/native failure | Completion faults with the original exception; pending work not already canceled faults with that cause |

Racing events are not a total order. For finite scheduler calls, cancellation of
the **wait** uses the caller token; cancellation of the **item** uses the effective
token. Which wins is determined by the first observed terminal task. Infinite
scheduler calls return the item task, so noncooperative running work can delay
caller cancellation. Faulted OCEs are not silently converted to cancellation by
the timeout wrapper.

## Ownership

Options are validated before native handles are allocated. The constructor owns
cleanup until thread start succeeds. The worker then owns native wait handles
and OLE teardown. A constructor initialization timeout closes admission and
requests shutdown; a late initializer exits after it returns.

`Completion` represents actual worker exit and preserves HRESULT/Win32 failure
information. It does not wait for arbitrary caller cancellation callbacks.
`ShutdownAsync` requests shutdown and bounds only the termination wait.
`Dispose` retains its potentially unbounded join, including repeated external
disposal after self-disposal. Shutdown cancellation callbacks execute outside
the lifecycle gate; a lease prevents their CTS from being disposed mid-signal.
Pending items retire before potentially blocking user cancellation callbacks.

One-off execution explicitly initializes OLE (including successful `S_FALSE`),
performs a bounded final pump, and uninitializes on the same STA. Its task completes
after owned cleanup. Primary failures win over cleanup failures. Internally hidden
tasks are fault-observed even if the caller stopped waiting. External tasks passed
to `TimeoutAfterAsync` remain caller-owned and are never canceled by the helper.

## Pump and queue policy

Each batch processes at most 64 messages. Scheduler work gets a turn between
batches. `MsgWaitForMultipleObjectsEx` with `MWMO_INPUTAVAILABLE` notices unread
old input. Borrowed pumps preserve `WM_QUIT` with its signed exit code; the owning
scheduler consumes it and stops. A single blocking callback can still block a
batch, just as a blocking delegate can.

`MaximumPendingWorkItems` is optional and excludes the active delegate. Admission,
dequeue, cancellation removal, and shutdown are synchronized. Full queues fault
new calls with `InvalidOperationException`; they never block for capacity.

Opt-in EventSource `AdaskoTheBeAsT-Interop-Threading` reports scheduler/work IDs,
state, pending count, queue wait and execution/wait duration in milliseconds.
`timed-out` followed later by `execution-ended` identifies work outliving its caller.
Events contain no arguments, results, COM payloads, exception messages, or mutex names.
Listener callbacks run off the STA and outside the admission gate. Work events
are asynchronous snapshots and may arrive out of order; correlate work IDs and
durations, not delivery order. Worker termination is emitted only after publishing
`Completion`, so a listener can safely inspect or wait for it.

## Compatibility

`ISingleThreadedApartmentTaskScheduler` is unchanged. New members are on the concrete
type and `ICooperativeStaTaskScheduler`. Default capacity is still unlimited.
Legacy mutex creation still grants Everyone FullControl and preserves existing ACLs.
The options API defaults to current-user synchronize/modify rights, session scope,
and failure on abandonment. Names in that API are unqualified; namespace comes from
options. Legacy local overloads preserve explicit `Local\`/`Global\` identities.
Acquisition cancellation wins before delegate invocation, even if ownership was
simultaneously acquired; ownership is always released on the same thread.

Intentional corrections: negative polling intervals now fail before condition
evaluation; oversized defaults fail during construction; one-off execution adds
explicit OLE ownership, post-execution cancellation, unrelated-OCE fault
classification, and completion-after-cleanup; worker initialization failures now
fail construction. Native `WM_QUIT` now shuts down the scheduler.
