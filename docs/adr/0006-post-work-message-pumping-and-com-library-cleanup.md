# ADR 0006: Opt-in bounded post-work-item message pumping and shutdown COM library cleanup

- Status: Accepted
- Date: 2026-11-16

## Context

In-process legacy STA COM servers (including Visual Basic 6 `MSVBVM60.DLL`, Borland Delphi VCL components using `Classes.MakeObjectInstance` hidden `HWND`s, and MDAC/Jet 4.0) rely on two Win32/COM housekeeping steps on their owning STA thread:

1. **Deferred message queue rundown after COM calls and RCW release**:
   - Releasing RCWs or completing STA method calls frequently posts hidden window (`WM_DESTROY`, custom user messages) or OLE channel rundown messages to the STA thread's message queue.
   - During continuous bursts of queued work items or right as `CreateThreadScope` disposes COM objects during scheduler shutdown, exiting or starting the next item without a bounded message drain can delay window teardown or leave final messages unprocessed before `CoUninitialize`.
2. **Unloading in-process COM server DLLs before apartment teardown**:
   - COM DLLs implementing `DllCanUnloadNow` remain mapped in the process unless `combase!CoFreeUnusedLibrariesEx(0, 0)` (followed by a message pump pass for any window cleanup triggered during `DllCanUnloadNow`) runs on that STA thread **after** RCWs/scopes are released and **before** `CoUninitialize` / `OleUninitialize`.
3. **Safety constraints on default behavior**:
   - Making post-work-item message pumping or `CoFreeUnusedLibrariesEx` active by default would alter reentrancy timing and shutdown cost for existing callers that do not host legacy in-process COM DLLs.
   - Any additional pump pass must remain strictly bounded so message floods cannot starve queued work or hang shutdown, and must preserve `WM_QUIT` (`PostQuitMessage`) so only the main `MessageLoopThread` consumes `WM_QUIT` and sets `QuitExitCode`.

## Decision

1. **Add opt-in options with conservative `0` / `false` defaults**:
   - `SingleThreadedApartmentTaskSchedulerOptions.PostWorkItemMessagePumpBatchSize` (`int`, default `0`): maximum number of pending Win32 messages pumped (`preserveQuit: true`) immediately after each dequeued work item finishes. Reject negative values at construction with `ArgumentOutOfRangeException`.
   - `SingleThreadedApartmentTaskSchedulerOptions.FreeUnusedComLibrariesOnShutdown` (`bool`, default `false`): when `true`, invoke `combase!CoFreeUnusedLibrariesEx(0, 0)` on the owned STA thread during shutdown.
2. **Preserve `WM_QUIT` and bound every pump batch**:
   - Extend `NativeMethods.PumpPendingMessages(bool preserveQuit = true, int maxMessages = DefaultMessageBatchSize)` and `StaPlatform.Pump(bool preserveQuit, int maxMessages)` to stop immediately on `maxMessages <= 0`, process at most `maxMessages` messages via `PeekMessageW(..., PM_REMOVE)`, and re-post `WM_QUIT` via `PostQuitMessage(exitCode)` whenever `preserveQuit` is `true`.
   - Only the main loop's primary `Pump(preserveQuit: false)` consumes `WM_QUIT`, records `QuitExitCode`, and initiates `Shutdown()`.
3. **Enforce explicit STA shutdown teardown order**:
   - Inside `TeardownOnStaThread` (running on the owned STA thread after `DrainQueue(failure)`):
     1. `threadScope.Dispose()` (if a scope was created).
     2. `StaPlatform.FreeUnusedLibraries()` (`CoFreeUnusedLibrariesEx(0, 0)`) when `staVerified && FreeUnusedComLibrariesOnShutdown`.
     3. One bounded final message pump (`StaPlatform.Pump(preserveQuit: true, batchSize)`) when `staVerified && (FreeUnusedComLibrariesOnShutdown || PostWorkItemMessagePumpBatchSize > 0)`, using `PostWorkItemMessagePumpBatchSize` when positive or `DefaultMessageBatchSize` (`64`) when triggered solely by `FreeUnusedComLibrariesOnShutdown`.
     4. `StaPlatform.UninitializeCom()` (`CoUninitialize`) if `InitializeComSta` succeeded.
     5. `StaPlatform.Uninitialize()` (`OleUninitialize`) if `Initialize` succeeded.
   - Wrap each step in `InvokeCleanupStep` so later native teardown steps still execute if an earlier step throws, while preserving the first observed primary failure.

## Why

- Defaulting to `0` and `false` guarantees zero behavior or performance change for callers who do not opt in.
- Running `Scope.Dispose()` -> `CoFreeUnusedLibrariesEx(0, 0)` -> bounded final pump -> `CoUninitialize()` -> `OleUninitialize()` matches the exact COM apartment rundown sequence required for `DllCanUnloadNow` and hidden STA windows to clean up cleanly before apartment uninitialization.
- Using `preserveQuit: true` in both post-work-item and teardown pumps prevents auxiliary drains from swallowing `WM_QUIT` before the scheduler loop can observe its exit code.

## Consequences

Positive:

- Legacy VB6, Delphi VCL, and Jet/ADO in-process STA COM servers can drain deferred window/channel messages deterministically and unload unused COM DLLs cleanly on scheduler disposal.
- Message floods remain bounded by `PostWorkItemMessagePumpBatchSize` (or `64` on shutdown library cleanup), preventing unbounded starvation.
- `WM_QUIT` exit-code capture (`QuitExitCode`) remains deterministic even when `PostWorkItemMessagePumpBatchSize > 0` is enabled.

Trade-offs:

- Enabling `PostWorkItemMessagePumpBatchSize > 0` introduces an additional reentrancy window after each work item; callers must still avoid holding non-reentrant invariants across pumped messages.
- Calling `CoFreeUnusedLibrariesEx(0, 0)` on shutdown synchronously invokes `DllCanUnloadNow` across loaded in-process COM servers on that thread, adding small teardown work when opted in.
