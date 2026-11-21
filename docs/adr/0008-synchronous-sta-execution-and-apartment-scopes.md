# ADR 0008: Synchronous STA execution, portable thread affinity, and standalone STA apartment scopes

- Status: Accepted
- Date: 2026-11-17

## Context

Auditing production native and STA COM wrappers revealed three closely related gaps across one-off STA execution, cross-process mutex composition, and externally hosted STA worker threads:

1. **Composing cross-process OS mutexes with STA COM work**:
   - `MutexHelper.RunInMutex` requires a synchronous delegate so OS mutex acquisition (`WaitOne` / `WaitAny`) and `ReleaseMutex()` stay on the same OS thread.
   - Because `SingleThreadedApartmentTask` only exposed asynchronous `Task<T>` methods (`RunAsync<T>` / `RunWithTimeoutAsync<T>`) and lacked `Action` (`void`) overloads, callers hand-rolled `new Thread` + `SetApartmentState(ApartmentState.STA)` + `Join()` helpers.
   - Those ad-hoc helpers skipped `OleInitialize`, skipped native `combase!CoGetApartmentType` verification and Nano Server `CO_E_NOTINITIALIZED` recovery (`CoInitializeEx(COINIT_APARTMENTTHREADED)`), skipped the bounded final message pump before teardown, and wrapped worker exceptions inconsistently.
   - Meanwhile, `SingleThreadedApartmentTask.RunAsync` itself did not yet run the native STA apartment verification added to `SingleThreadedApartmentTaskScheduler` in [ADR 0005](0005-native-sta-verification-and-thread-scope-lifecycle.md).
2. **Thread-ownership assertions in stateful sessions**:
   - Stateful native and COM session wrappers repeatedly duplicated `_owningThreadId = Environment.CurrentManagedThreadId` and `EnsureOwningThread()` guards to detect accidental cross-thread calls.
   - A `default` value-type guard where `_owningThreadId == 0` must fail closed rather than accidentally matching any managed thread.
3. **Explicit STA COM/OLE leases on external STA worker threads**:
   - When sessions run on externally managed STA threads (such as multi-worker pools with `ApartmentState.STA` or `[StaFact]` test threads), CoreCLR initializes worker threads via internal `RoInitialize(0x40000000)`, which skips registering `"OleMainThreadWndClass"` in `combase.dll` until an explicit `CoInitializeEx(COINIT_APARTMENTTHREADED)` or `OleInitialize` call runs on that thread. Without it, contended `Lock` or `WaitHandle` waits entering `CoWaitForMultipleHandles -> GetOrCreateSTAWindow` can fail.
   - Callers hosting sessions on external STA threads need a balanced, idempotent, thread-affinitized COM/OLE scope without creating a second `SingleThreadedApartmentTaskScheduler` thread.

## Decision

1. **Add portable `ThreadAffinity` (`readonly struct`, `IEquatable<ThreadAffinity>`)**:
   - Expose `OwningThreadId`, `IsInitialized`, `IsCurrentThread`, `ThreadAffinity.Capture()`, and `ThrowIfWrongThread(string? ownerName = null)` across all target frameworks without `[SupportedOSPlatform("windows")]`.
   - Store a single 4-byte `int` with zero heap allocation. Because `Environment.CurrentManagedThreadId` is strictly positive (`>= 1`), `default(ThreadAffinity)` (`OwningThreadId == 0`) reports `IsInitialized == false`, `IsCurrentThread == false`, and throws `InvalidOperationException` from `ThrowIfWrongThread`.
2. **Add Windows `StaApartment` and `StaApartmentScope`**:
   - `StaApartment.IsCurrentThreadSta(bool verifyNativeApartment = true)` and `StaApartment.VerifyCurrentThreadSta(bool verifyNativeApartment = true)` check `Thread.CurrentThread.GetApartmentState() == ApartmentState.STA` and, when `verifyNativeApartment` is `true`, query `combase!CoGetApartmentType` (accepting `APTTYPE_STA`, `APTTYPE_MAINSTA`, and STA-hosted `APTTYPE_NA` with `APTTYPEQUALIFIER_NA_ON_STA` or `APTTYPEQUALIFIER_NA_ON_MAINSTA`).
   - `StaApartment.EnterScope(bool initializeOle = false)` rejects managed MTA or native `RPC_E_CHANGED_MODE` / `APTTYPE_MTA` with `InvalidOperationException` (preserving any `COMException` as `InnerException`), calls `CoInitializeEx(IntPtr.Zero, COINIT_APARTMENTTHREADED)` to increment `combase`'s apartment reference count and register `"OleMainThreadWndClass"`, optionally calls `OleInitialize(IntPtr.Zero)` (rolling back `CoUninitialize()` on failure), verifies native STA membership, and returns a `StaApartmentScope` bound to `ThreadAffinity.Capture()`.
   - `StaApartmentScope.Dispose()` rejects cross-thread disposal with `InvalidOperationException` without corrupting the wrong thread's COM reference count, and uninitializes at most once (`OleUninitialize()` then `CoUninitialize()`) on the owning thread.
3. **Bring `SingleThreadedApartmentTask` to native STA verification parity and add synchronous `Run` + `StaExecutionMode`**:
   - Share `StaPlatform.EnsureNativeStaApartment` across `SingleThreadedApartmentTaskScheduler` and `SingleThreadedApartmentTask` (`VerifyNativeStaApartment = true` by default).
   - Introduce `StaExecutionMode`:
     - `DedicatedThread` (`0`, default): spawns a dedicated background STA thread, runs `OleInitialize` and native STA verification, invokes the delegate, performs a bounded final pump (`preserveQuit: false`), runs balanced `CoUninitialize` / `OleUninitialize`, **always joins** the thread before returning, and rethrows worker failures via `ExceptionDispatchInfo.Capture(failure).Throw()`.
     - `ReuseCurrentStaThread` (`1`): when `StaApartment.IsCurrentThreadSta(verifyNativeApartment: false)` is `true`, executes inline on the current STA thread inside `StaApartment.EnterScope(initializeOle: true)` **without** running a `preserveQuit: false` teardown pump on the caller's message queue; when called from an MTA or uninitialized thread, falls back to `DedicatedThread`.
4. **Add ergonomic `Action` (`void`) overloads without breaking interfaces**:
   - Add `Action` and `Action<StaYield>` overloads to `SingleThreadedApartmentTask` (`Run`, `RunAsync`, `RunWithTimeoutAsync`) and `MutexHelper.RunInMutex`.
   - Add `SingleThreadedApartmentTaskSchedulerExtensions.RunAsync` extension methods on `ISingleThreadedApartmentTaskScheduler` so existing external implementations of `ISingleThreadedApartmentTaskScheduler` remain binary- and source-compatible.

## Why

- Always joining any dedicated STA thread inside synchronous `SingleThreadedApartmentTask.Run` guarantees that an enclosing `MutexHelper.RunInMutex(...)` block never releases its cross-process OS mutex while native code is still mutating protected state.
- Defaulting `StaExecutionMode` to `DedicatedThread` preserves message-queue isolation and avoids unexpected reentrancy when called from a UI STA thread, while `ReuseCurrentStaThread` gives STA worker pools and `[StaFact]` tests a zero-thread-spawn inline fast path that leaves `WM_QUIT` and pending caller messages intact.
- Sharing `StaPlatform` apartment verification across `SingleThreadedApartmentTaskScheduler`, `SingleThreadedApartmentTask`, and `StaApartment` eliminates behavioral drift on Windows Server and Windows Nano Server.

## Consequences

Positive:

- Callers can compose `MutexHelper.RunInMutex(..., () => SingleThreadedApartmentTask.Run(() => ...))` directly without hand-rolled STA thread plumbing or dummy return values.
- Stateful sessions hosted on external STA worker pools can enforce thread ownership via `ThreadAffinity` and acquire balanced `"OleMainThreadWndClass"`-registered COM/OLE leases via `StaApartment.EnterScope()`.
- Original worker exception types and stack traces are preserved across synchronous `Run` calls via `ExceptionDispatchInfo`.

Trade-offs:

- Because synchronous `SingleThreadedApartmentTask.Run` always joins its dedicated STA thread once started, a pre-canceled `CancellationToken` exits immediately before thread creation, whereas mid-call cancellation remains cooperative and waits for the synchronous delegate and COM/OLE teardown to return before throwing `OperationCanceledException`.
