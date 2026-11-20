# ADR 0010: `ValueTask` single-consumption timeouts, cooperative linked deadlines, and STA thread stack sizing

- Status: Accepted
- Date: 2026-11-17

## Context

Two remaining interop execution concerns surfaced across high-throughput pooled dispatchers and 32-bit native calculation libraries:

1. **Single-consumption `ValueTask` timeouts and cooperative deadline cancellation**:
   - `TaskExtension.TimeoutAfterAsync` previously accepted only `Task` and `Task<TResult>` without canceling the underlying operation.
   - Modern `.NET` dispatchers frequently return `ValueTask` or `ValueTask<TResult>` backed by pooled `IValueTaskSource` / `ManualResetValueTaskSourceCore<T>` instances, and callers also need cooperative `Func<CancellationToken, Task>` / `Func<CancellationToken, ValueTask>` helpers that link caller cancellation with a `TimeSpan` deadline while still classifying deadline expiry as `TimeoutException` rather than `OperationCanceledException`.
   - Adding `ValueTask` support to `net472`–`net481` would require referencing `System.Threading.Tasks.Extensions`, violating the repository's zero-new-NuGet-dependency constraint (`System.Threading.AccessControl` must remain the sole external package dependency).
   - For pooled `IValueTaskSource` instances, calling `GetResult` more than once (or calling `GetStatus`/`GetResult` after `GetResult` has recycled the source) corrupts pooled state or throws `InvalidOperationException`.
2. **Dedicated STA thread stack sizing (`MaxStackSize`)**:
   - 32-bit Compaq Visual Fortran, Borland Delphi, PowerBASIC, and VB6 libraries allocate large native stack frames that can overflow constrained IIS/container default stacks (256 KB–512 KB), while `win-x86` processes operating inside a 2 GB virtual address ceiling also need explicit control over per-thread stack reservation (`new Thread(start, maxStackSize)`).

## Decision

1. **Add cooperative linked-deadline `TimeoutAfterAsync` overloads across all five target frameworks**:
   - Add `TaskExtension.TimeoutAfterAsync(Func<CancellationToken, Task>, TimeSpan, CancellationToken = default)` and `TaskExtension.TimeoutAfterAsync<TResult>(Func<CancellationToken, Task<TResult>>, TimeSpan, CancellationToken = default)`.
   - Validate `operation` (`ArgumentNullException`) and `timeout` (`TimeoutValidation.Validate`) first, and annotate the `Task`-returning delegate overloads with `[OverloadResolutionPriority(1)]` on `NET9_0_OR_GREATER` so untyped `async ct => ...` lambdas resolve unambiguously to `Func<CancellationToken, Task>` / `Func<CancellationToken, Task<TResult>>` while delegates returning `ValueTask` / `ValueTask<TResult>` bind cleanly to the `ValueTask` overloads.
   - Fast-path pre-canceled `cancellationToken` (`Task.FromCanceled`) without invoking `operation`, and fast-path `Timeout.InfiniteTimeSpan` by invoking `operation(cancellationToken)` directly without allocating a linked `CancellationTokenSource` or timer.
   - For finite timeouts, create a linked `CancellationTokenSource(cancellationToken)` armed immediately via `Cancel()` when `timeout == TimeSpan.Zero` or scheduled with `CancelAfter(timeout)` when positive, dispose it only after the underlying task settles, attach `TaskWait.ObserveFault` if the caller abandons the wait, and classify outcomes deterministically:
     - Successful completion or non-deadline fault (including an unrelated faulted `OperationCanceledException`) preserves the source outcome.
     - Caller `cancellationToken.IsCancellationRequested` transitions to `Canceled` with `cancellationToken`.
     - Deadline expiry without caller cancellation faults with `TimeoutException`, even when `operation` cooperatively canceled or threw `OperationCanceledException` for the linked deadline token.
2. **Add `ValueTask` and `ValueTask<TResult>` overloads under `#if NET8_0_OR_GREATER` (`net10.0` and `net11.0`)**:
   - Expose `TimeoutAfterAsync(this ValueTask, ...)`, `TimeoutAfterAsync<TResult>(this ValueTask<TResult>, ...)`, `TimeoutAfterAsync(Func<CancellationToken, ValueTask>, ...)`, and `TimeoutAfterAsync<TResult>(Func<CancellationToken, ValueTask<TResult>>, ...)` on `net10.0` and `net11.0` with zero added package dependencies on `.NET Framework`.
   - Enforce strict single consumption of `IValueTaskSource`:
     - Validate arguments first.
     - When `task.IsCompletedSuccessfully` is `true`, consume `task.GetAwaiter().GetResult()` synchronously **once** and return `ValueTask.CompletedTask` / `new ValueTask<TResult>(result)` with **zero heap allocation** (no `.AsTask()`, no `TaskCompletionSource`, no timer).
     - Otherwise materialize `task.AsTask()` **at most once**, attach `TaskWait.ObserveFault` on wait abandonment so late faults on pooled sources never raise `UnobservedTaskException`, and wrap the monitored `Task` in `new ValueTask(...)`.
3. **Add `MaxStackSize` and `SingleThreadedApartmentTaskOptions`**:
   - Add `MaxStackSize` (`int`, default `0` = runtime/PE default; negative values throw `ArgumentOutOfRangeException`) to `SingleThreadedApartmentTaskSchedulerOptions` and construct the worker via `new Thread(ThreadEntry, maxStackSize)` when positive.
   - Add `SingleThreadedApartmentTaskOptions` (`ThreadName`, `MaxStackSize`, `ExecutionMode`, `BeforeThreadStart`, `VerifyNativeStaApartment`, `ThreadCulture`, `ThreadUICulture`, `WindowsThreadLocaleId`, `ResetFloatingPointMaskBeforeExit`) and `SingleThreadedApartmentTask.Run` / `RunAsync` overloads accepting it so one-off STA callers can configure stack size, apartment verification, locale, and `win-x86` FPU cleanup without combinatorial overload growth.

## Why

- Guarding `ValueTask` APIs with `#if NET8_0_OR_GREATER` delivers zero-allocation fast paths and pooled `IValueTaskSource` safety on `net10.0` and `net11.0` while keeping `.NET Framework` NuGet dependencies completely unchanged.
- Keeping the linked deadline `CancellationTokenSource` alive until the underlying task settles prevents `ObjectDisposedException` inside cooperative operations that register late callbacks or inspect token properties after timeout abandonment.
- Translating cooperative deadline cancellation (`OperationCanceledException(deadline.Token)`) into `TimeoutException` gives callers one consistent timeout exception contract whether an operation ignores cancellation or cooperates with the deadline token.

## Consequences

Positive:

- Pooled `ManualResetValueTaskSourceCore<T>` operations are consumed strictly once across synchronous completion, asynchronous completion, timeout, caller cancellation, and late post-timeout faults.
- Already-completed `ValueTask` and `ValueTask<TResult>` calls pass through `TimeoutAfterAsync` with zero heap allocations.
- 32-bit Fortran, Delphi, PowerBASIC, and VB6 STA workloads can request explicit thread stack sizes on both `SingleThreadedApartmentTaskScheduler` and `SingleThreadedApartmentTask`.

Trade-offs:

- `ValueTask` timeout overloads are available on `net10.0` and `net11.0` only; `.NET Framework` callers continue using the `Task` and `Func<CancellationToken, Task>` overloads.
- As on all `.NET` runtimes on Windows, `new Thread(start, maxStackSize)` honors positive `maxStackSize` values subject to OS minimum stack granularity and process virtual memory limits, and only applies when a dedicated thread is created (not when `StaExecutionMode.ReuseCurrentStaThread` reuses an existing STA thread).
