# ADR 0001: Harden timeout cancellation and STA scheduler dispatch

- Status: Accepted
- Date: 2026-04-04

## Context

Two reliability issues were identified in the threading helpers:

1. `TaskExtension.TimeoutAfterAsync` created a linked cancellation token source, but the timeout wait used `timeoutCts.Token` instead of the linked token. That meant caller cancellation did not stop the timeout wait and could be reported as a timeout instead of cancellation.
2. `SingleThreadedApartmentTaskScheduler` stored queued work as `Delegate` and completed callers through `ContinueWith(..., cancellationToken)`. This had two drawbacks:
   - the returned task could be canceled after work was already queued, even if the work still executed later on the STA thread;
   - `DynamicInvoke()` wrapped user exceptions and added reflection overhead.

## Decision

We changed the implementation in two places:

### `TaskExtension.TimeoutAfterAsync`

- use the linked token for `Task.Delay`;
- treat completion of the original task as success;
- if the delay completes first, first check `cancellationToken.ThrowIfCancellationRequested()` and only then throw `TimeoutException`.

### `SingleThreadedApartmentTaskScheduler`

- replace the queue of raw delegates with typed work items (`IStaWorkItem` / `StaWorkItem<T>`);
- return the typed `Task<T?>` directly instead of projecting through `ContinueWith`;
- register cancellation on the queued work item so cancellation is honored before execution;
- execute the delegate directly on the STA thread so the original exception is preserved.

## Why

These changes make the library behavior match caller expectations:

- cancellation stays cancellation;
- timeouts stay timeouts;
- queued STA work cannot appear canceled just because the continuation token was canceled after enqueue;
- callers receive the original exception rather than a reflection wrapper;
- the scheduler avoids unnecessary reflection overhead on every dispatch.

## Consequences

Positive:

- more predictable timeout and cancellation semantics;
- better exception fidelity for debugging and error handling;
- lower dispatch overhead in the STA scheduler;
- stronger regression coverage for cancellation and exception propagation.

Trade-offs:

- the scheduler now uses two small internal support types;
- the implementation is slightly more explicit, but the behavior is safer.
