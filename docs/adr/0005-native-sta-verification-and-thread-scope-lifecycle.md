# ADR 0005: Native COM STA apartment verification, pre-start hook, and owned thread scopes

- Status: Accepted
- Date: 2026-11-16

## Context

Production hosting of registration-free 32-bit and 64-bit STA COM components on Windows Server and Windows Nano Server 2025 exposed two lifecycle gaps in `SingleThreadedApartmentTaskScheduler` while requiring `AdaskoTheBeAsT.Interop.Threading` to remain free of package dependencies on `AdaskoTheBeAsT.Interop.COM`:

1. **Thread-startup COM apartment race on Windows Nano Server**:
   - `StaPlatform.Start(Thread)` sets `Thread.SetApartmentState(ApartmentState.STA)` and calls `Thread.Start()`.
   - On CoreCLR, `Thread.Start()` invokes `combase!CoInitializeEx(COINIT_APARTMENTTHREADED)` at native OS thread startup **before** the managed `ThreadEntry` method begins executing.
   - On Windows Nano Server images lacking `win32u.dll`, if neither an OS-level shim nor `AdaskoTheBeAsT.Interop.COM`'s in-process STA shim (`NanoServerOptions.RefreshStaState`) has been initialized before `Thread.Start()`, CoreCLR's implicit STA initialization can fail or fall back to MTA (`APTTYPE_MTA`) or leave COM uninitialized (`CO_E_NOTINITIALIZED`) even though `Thread.GetApartmentState()` still reports `ApartmentState.STA`.
2. **Repeated per-call SxS / COM activation overhead and lifecycle pairing**:
   - Without a thread-affinitized scope hook, callers combining `SingleThreadedApartmentTaskScheduler` with `AdaskoTheBeAsT.Interop.COM` had to enter and exit side-by-side activation contexts (`CreateActCtxW` / `CoRegisterClassObject`) inside every `RunAsync` delegate, paying repeated activation costs and preventing long-lived COM objects from staying bound to the STA worker across calls.
   - Two design candidates were considered for owned-thread lifecycle hooks: a pair of `Action? OnThreadInitialized` / `Action? OnThreadShuttingDown` callbacks versus a single `Func<IDisposable?>? CreateThreadScope` factory. Separate init/exit callbacks force callers to coordinate mutable state across lambdas and make it easy to run shutdown logic even when initialization never completed or threw partway through.

## Decision

1. **Snapshot options and run `BeforeThreadStart` on the constructing thread before `Thread.Start()`**:
   - Capture all `SingleThreadedApartmentTaskSchedulerOptions` properties into immutable local/field values at the start of construction so mutations made during or after `BeforeThreadStart` cannot alter scheduler behavior.
   - Invoke `BeforeThreadStart` (`Action?`) synchronously on the caller thread after validation and wait-handle creation, immediately before `_platform.Start(_thread)`. If `BeforeThreadStart` throws, dispose allocated handles and abort construction without starting the background thread.
2. **Verify native COM STA apartment state (`VerifyNativeStaApartment = true` by default)**:
   - Preserve `StaPlatform.Initialize()` (`OleInitialize`) on the worker thread.
   - When `VerifyNativeStaApartment` is `true`, query `combase!CoGetApartmentType(out aptType, out aptQualifier)`:
     - On `CO_E_NOTINITIALIZED`, call `combase!CoInitializeEx(IntPtr.Zero, COINIT_APARTMENTTHREADED)`, propagate `RPC_E_CHANGED_MODE` or any negative HRESULT as `COMException`, record COM initialization ownership, and re-query `CoGetApartmentType`.
     - Accept `APTTYPE_STA`, `APTTYPE_MAINSTA`, and STA-hosted neutral apartments (`APTTYPE_NA` with `APTTYPEQUALIFIER_NA_ON_STA` or `APTTYPEQUALIFIER_NA_ON_MAINSTA`).
     - Fault construction with `InvalidOperationException` (including remediation guidance mentioning `BeforeThreadStart` / Nano Server STA shims) if the worker thread is in `APTTYPE_MTA` or another non-STA apartment.
     - Balance teardown strictly: call `CoUninitialize()` only if `InitializeComSta()` succeeded, and call `OleUninitialize()` only if `Initialize()` succeeded. Preserve non-Windows/simulated `StaPlatform` behavior for unit tests.
3. **Adopt `CreateThreadScope` (`Func<IDisposable?>?`) as the sole thread lifecycle API**:
   - Do not add separate `OnThreadInitialized` / `OnThreadShuttingDown` callbacks.
   - Invoke `CreateThreadScope` at most once on the owned STA thread after OLE/STA verification (and only if shutdown was not already requested during a constructor initialization timeout) before signaling `_threadReady`.
   - If `CreateThreadScope` throws, fault `_threadReady` and `Completion` with that original exception and run balanced COM/OLE teardown without invoking `Dispose()` on a non-existent scope.
   - During shutdown in `ThreadEntry`'s `finally` block, call `DrainQueue(failure)` **before** `TeardownOnStaThread(...)` so that:
     - Pending queued items are canceled (on normal shutdown) before `scope.Dispose()` runs on the owned STA thread.
     - If `scope.Dispose()` throws, `Completion` and `ShutdownAsync` fault with that disposal exception while already-canceled pending items remain canceled rather than being retroactively faulted.

## Why

- Running `BeforeThreadStart` on the constructing thread before `Thread.Start()` is the only point early enough to install in-process Nano Server STA hooks before CoreCLR's native thread-startup `CoInitializeEx(STA)` executes.
- Verifying `CoGetApartmentType` catches silent managed-vs-native apartment mismatches immediately at scheduler construction rather than allowing thread-affine STA coclasses to corrupt state or fail intermittently on an implicit MTA thread.
- `Func<IDisposable?>? CreateThreadScope` pairs acquisition and STA-affinitized release in a single standard .NET abstraction (`IDisposable`), avoids any package dependency on `AdaskoTheBeAsT.Interop.COM`, and guarantees `Dispose()` runs only when scope creation succeeded.

## Consequences

Positive:

- Dependency-free one-line Nano Server startup wiring (`BeforeThreadStart = NanoServerOptions.RefreshStaState`).
- Early, actionable constructor failures when native STA initialization is broken or overridden to MTA.
- Per-thread SxS manifests, COM class factories, and session objects can be created once and disposed deterministically on the owning STA thread.
- Existing shutdown and pending-item cancellation contracts from [ADR 0002](0002-sta-lifetime-and-cancellation-contracts.md) remain intact.

Trade-offs:

- Custom non-COM hosts that deliberately intercept `StaPlatform` to run worker threads in native MTA must set `VerifyNativeStaApartment = false`.
- Like user work items, a blocking `CreateThreadScope` or `scope.Dispose()` implementation runs synchronously on the STA thread; constructor initialization timeout stops the caller's wait and requests shutdown, while `Dispose()` still joins the worker thread to protect thread-affinitized native state.
