# Changelog

Notable changes to `AdaskoTheBeAsT.Interop.Threading`.

Entries before 4.0.0 are reconstructed from local Git tags and their source diffs; dates shown are tag commit dates, not verified NuGet publication dates.

See the [migration guide](MIGRATION.md) for upgrade steps.

## [5.0.0] - 2026-11-17

### Breaking changes

- Target `.NET 11` (`net11.0`) and remove `net9.0` and `net8.0`. Retain `net10.0`, `net481`, `net48`, and `net472`.
- Verify native COM STA apartment state by default (`VerifyNativeStaApartment = true`) on both `SingleThreadedApartmentTaskScheduler` and `SingleThreadedApartmentTask` (`Run` / `RunAsync`). After `OleInitialize`, the worker checks `combase!CoGetApartmentType`, recovers `CO_E_NOTINITIALIZED` via balanced `CoInitializeEx(COINIT_APARTMENTTHREADED)`, and fails with `InvalidOperationException` if the worker thread is in native MTA instead of STA.
- Reject negative `PostWorkItemMessagePumpBatchSize`, negative `MaxStackSize`, and non-positive `WindowsThreadLocaleId` values during option validation with `ArgumentOutOfRangeException`.

### Added

- Synchronous one-off STA execution via `SingleThreadedApartmentTask.Run` (`Action`, `Action<StaYield>`, `Func<T>`, `Func<StaYield, T>`), `StaExecutionMode` (`DedicatedThread` default and `ReuseCurrentStaThread` inline fast path preserving caller `WM_QUIT`), and `SingleThreadedApartmentTaskOptions`, always joining dedicated STA threads before returning so enclosing `MutexHelper.RunInMutex` scopes never release cross-process locks early.
- `Action` (`void`) overloads on `SingleThreadedApartmentTask` (`RunAsync`, `RunWithTimeoutAsync`), `MutexHelper.RunInMutex` (all four overload shapes), and `SingleThreadedApartmentTaskSchedulerExtensions.RunAsync` (preserving binary and source compatibility for existing `ISingleThreadedApartmentTaskScheduler` implementations).
- Portable `ThreadAffinity` (`readonly struct`, zero heap allocation, closed failure on `default(ThreadAffinity)`) and Windows `StaApartment` / `StaApartmentScope` (`IsCurrentThreadSta`, `VerifyCurrentThreadSta`, and thread-affinitized, idempotent `EnterScope(bool initializeOle = false)`).
- Managed culture and Win32 kernel thread locale pinning/hygiene via `SingleThreadedApartmentTaskSchedulerOptions.ThreadCulture`, `ThreadUICulture`, `WindowsThreadLocaleId` (`kernel32!SetThreadLocale` applied before `CreateThreadScope`), `ResetThreadCultureAndLocaleAfterWorkItem`, and standalone `ThreadCultureScope.Enter`.
- Cooperative linked-deadline `TaskExtension.TimeoutAfterAsync(Func<CancellationToken, Task>, ...)` and `Func<CancellationToken, Task<TResult>>` overloads across all five target frameworks, plus `#if NET8_0_OR_GREATER` (`net10.0` and `net11.0`) `ValueTask` and `ValueTask<TResult>` overloads enforcing zero-allocation synchronous fast paths and strict single consumption of pooled `IValueTaskSource` instances with zero added NuGet dependencies.
- Configurable `MaxStackSize` (`int`, default `0`) on `SingleThreadedApartmentTaskSchedulerOptions` and `SingleThreadedApartmentTaskOptions` for deep 32-bit native Fortran/Delphi/VB6/PowerBASIC stack frames.
- `SingleThreadedApartmentTaskSchedulerOptions.BeforeThreadStart` (`Action?`) to run synchronous host preparation (such as `NanoServerOptions.RefreshStaState`) on the constructing thread after option snapshotting and before `Thread.Start()`.
- `SingleThreadedApartmentTaskSchedulerOptions.VerifyNativeStaApartment` (`bool`, default `true`) to control native COM STA apartment verification and `CO_E_NOTINITIALIZED` recovery.
- `SingleThreadedApartmentTaskSchedulerOptions.CreateThreadScope` (`Func<IDisposable?>?`) to create a thread-affinitized scope once on the owned STA thread before work runs and dispose it once on that exact STA thread during shutdown.
- Opt-in `SingleThreadedApartmentTaskSchedulerOptions.PostWorkItemMessagePumpBatchSize` (`int`, default `0`) and `FreeUnusedComLibrariesOnShutdown` (`bool`, default `false`) for bounded post-work-item and shutdown message pumping (`WM_QUIT`-preserving) plus `CoFreeUnusedLibrariesEx(0, 0)` teardown ordering.
- Opt-in `SingleThreadedApartmentTaskSchedulerOptions.ResetFloatingPointMaskAfterWorkItem` (`bool`, default `false`) on Windows `x86` (`Architecture.X86`) to clear pending x87/SSE status flags (`ucrtbase!_clearfp`) and restore all six floating-point exception masks (`_MCW_EM` via `ucrtbase!__control87_2`) after `CreateThreadScope` and each work item without altering callers' rounding or precision modes.
- Comprehensive Portable, Windows x64, and Windows x86 contract tests covering synchronous STA execution, `MutexHelper` + `SingleThreadedApartmentTask.Run` non-overlap under cancellation, `ThreadAffinity`, `StaApartmentScope`, culture and `SetThreadLocale` hygiene, single-consumption `IValueTaskSource` and cooperative deadline timeouts, `MaxStackSize` deep stack frames, native apartment verification, `CreateThreadScope`, post-work pumping, and 32-bit `win-x86` FPU trap poisoning (`scripts/Test-FpuProcess.ps1`).

### Changed

- Upgrade SDK to `11.0.100`, C# `LangVersion` to `14.0`, `System.Threading.AccessControl` ranges (`[11.0.0,12.0.0)` for `net11.0` and `.NET Framework`, `[10.0.15,11.0.0)` for `net10.0`), build analyzers, and `xunit.v3` (`4.0.3`) test packages.
- Expand [COM and Execution integration guidance](docs/using-with-adaskothebeast-interop-com.md) with Nano Server `BeforeThreadStart` wiring, `CreateThreadScope` COM activation scopes, cross-process `MutexHelper.RunInMutex` + `SingleThreadedApartmentTask.Run` composition, `ExecutionWorkerPool<TSession>` session guards (`ThreadAffinity` + `StaApartment.EnterScope` + `ThreadCultureScope`), culture/`SetThreadLocale`/`MaxStackSize` configuration, and explicit non-preemption timeout/cancellation contracts.

See [ADR 0004](docs/adr/0004-net11-target-baseline-and-major-version-5.md), [ADR 0005](docs/adr/0005-native-sta-verification-and-thread-scope-lifecycle.md), [ADR 0006](docs/adr/0006-post-work-message-pumping-and-com-library-cleanup.md), [ADR 0007](docs/adr/0007-windows-x86-floating-point-hygiene.md), [ADR 0008](docs/adr/0008-synchronous-sta-execution-and-apartment-scopes.md), [ADR 0009](docs/adr/0009-sta-thread-culture-and-windows-locale-hygiene.md), and [ADR 0010](docs/adr/0010-valuetask-and-cooperative-deadline-timeouts.md) for the rationale.

## [4.0.1] - 2026-10-24

### Changed

- Bump `System.Threading.AccessControl` minimum patch ranges to `[10.0.13,11.0.0)` on `net10.0` and `[9.0.21,10.0.0)` on `net9.0`.
- Upgrade build analyzers (`StyleCop.Analyzers`, `Microsoft.VisualStudio.Threading.Analyzers`, `AdaskoTheBeAsT.CodeCracker.CSharp`, `Meziantou.Analyzer`, `Microsoft.CodeAnalysis.NetAnalyzers`, `Roslynator.*`, `SonarAnalyzer.CSharp`) and unit-test packages (`Microsoft.NET.Test.Sdk`, `Microsoft.Testing.Extensions.GitHubActionsReport`, `Moq`, `xunit.v3`, `xunit.analyzers`, `xunit.runner.visualstudio`).
- Remove `RestorePackagesWithLockFile` and project `packages.lock.json` files so dependency and analyzer updates do not drift across library, test, sample, and benchmark projects.
- Disable `SA1121` in `AdaskoTheBeAsT.ruleset` to preserve explicit `IntPtr`/`UIntPtr` types in Win32 interop declarations, and apply targeted `#pragma warning disable` for `VSTHRD002`/`VSTHRD003` on task completion properties and post-`WhenAny` result extraction.
- Simplify `.NET Framework` condition in `test/Directory.Build.props` and remove redundant runtime OS checks from Windows-only test fixtures.

See [ADR 0003](docs/adr/0003-analyzer-baseline-and-lockfile-removal.md) for the rationale.

## [4.0.0]

### Breaking changes

- Raise the .NET Framework minimum to **4.7.2**, removing `net462`, `net47`, and `net471` assets. Retain `net472`, `net48`, `net481`, `net8.0`, `net9.0`, and `net10.0`.
- Fail scheduler construction on worker/OLE initialization failure instead of deferring the failure to a later scheduling call.
- Make one-off STA tasks explicitly own OLE initialization and teardown, check cancellation after delegate execution, and complete only after cleanup. An unrelated `OperationCanceledException` now faults the task rather than being treated as caller cancellation.
- Reject negative `StaYield.SpinUntil` polling intervals before evaluating the condition, and reject invalid default work-item timeouts during scheduler construction.
- Tighten mutex-name validation. Names must be non-empty, contain no null characters or embedded namespace separators, and fit the 260-character limit including any namespace prefix. Legacy `isGlobal: false` calls still accept explicit `Local\` and `Global\` prefixes.
- Treat scheduler-owned `WM_QUIT` as a shutdown request.

### Added

- `ICooperativeStaTaskScheduler`, without adding required members to `ISingleThreadedApartmentTaskScheduler`.
- `RunCooperativeAsync` with a token linked to caller cancellation, timeout, and shutdown.
- `Completion` and `ShutdownAsync` for observing worker termination and bounding the termination wait. The concrete scheduler also exposes `PendingWorkItemCount` and `QuitExitCode`.
- Optional `MaximumPendingWorkItems` queue capacity and opt-in EventSource diagnostics.
- Cancelable `StaYield.Sleep` and `SpinUntil` overloads, including a timeout-aware condition wait.
- Non-generic `Task.TimeoutAfterAsync`.
- `MutexExecutionOptions` with cancelable acquisition, explicit creation ACLs, namespace selection, and abandonment policy. Defaults are session scope, current-user synchronize/modify rights, and failure on abandonment. Legacy overload defaults remain unchanged.
- Contract tests, locked dependency inputs, hosted-service and mutex-process samples, benchmark scenarios, and a dedicated validation workflow.
- Standalone changelog and migration guide, with a shorter README organized around API selection and safe usage.

### Fixed

- Synchronize queue admission, pending cancellation, and shutdown; remove canceled pending work and release its captured delegate.
- Preserve original worker failures, delegate exceptions, and faulted `OperationCanceledException` task status.
- Validate null tasks and timeout ranges consistently, including completed source tasks; add completed-source and infinite-wait fast paths.
- Bound each native pump batch to 64 messages and give scheduler work a turn between batches. Detect unread input with `MWMO_INPUTAVAILABLE`.
- Preserve the signed `WM_QUIT` exit code in borrowed pumps; consume the message in the owning scheduler.
- Harden initialization and cleanup ownership, including late initialization and internally owned tasks that fault after the caller stops waiting.

### Validation status

Version 4.0.0 targets six frameworks. Earlier nine-target validation results do **not** validate this configuration. The validation harness uses the six supported targets on x64 and `net472` on x86 with the selected Microsoft.Testing.Platform runner. The current build configuration no longer enables the earlier 3.1.0 package/API comparison. See [validation notes](docs/validation.md) for recorded results and outstanding checks.

## [3.1.0] - 2026-04-20

### Changed

- Replace modern `net10.0-windows`, `net9.0-windows`, and `net8.0-windows` package targets with plain `net10.0`, `net9.0`, and `net8.0`.
- Add `[SupportedOSPlatform("windows")]` annotations to Windows-specific APIs on modern .NET, allowing portable projects to reference the package while receiving platform-analyzer guidance.
- Keep `TaskExtension` and scheduler options portable. Windows runtime behavior is unchanged by the target/annotation change.
- Retain the six .NET Framework targets: `net481`, `net48`, `net472`, `net471`, `net47`, and `net462`.

## [3.0.0] - 2026-04-20

### Breaking changes

- Convert `SingleThreadedApartmentTaskScheduler` from a static class to a disposable instance. Replace static `RunAsync` and `Shutdown` calls with calls on an owned instance.
- Replace the `netstandard2.0` asset with explicit .NET Framework targets from 4.6.2 through 4.8.1.

### Added

- `ISingleThreadedApartmentTaskScheduler` for dependency injection and test doubles.
- `SingleThreadedApartmentTaskSchedulerOptions` with `ThreadName` and `DefaultWorkItemTimeout`.
- Per-call scheduler timeout overload and multiple independent STA scheduler instances.
- Disposal that cancels queued work and joins the worker, with rejected scheduling after disposal.

### Fixed

- Surface OLE initialization failures to scheduling callers.
- Improve scheduler lifecycle/cancellation handling and exception preservation.
- Apply mutex creation ACLs atomically on modern .NET and .NET Framework without rewriting existing ACLs.
- Use `Stopwatch`-based timing in `StaYield` and harden timeout validation.

## [2.1.0] - 2026-04-07

### Changed

- Expand public XML documentation for mutexes, STA execution, scheduling, message pumping, and task timeouts.
- Improve the README and package description.

## [2.0.0] - 2026-04-07

### Fixed

- Honor caller cancellation in `TimeoutAfterAsync` instead of reporting it as a timeout.
- Replace reflection-based scheduler dispatch with typed work items, preserving original delegate exceptions.
- Return the queued work item's task directly and honor cancellation before execution instead of canceling only a projection continuation.

See [ADR 0001](docs/adr/0001-hardening-timeouts-and-sta-scheduler.md) for the rationale.

## [1.0.0] - 2025-11-23

- First tagged release, providing named mutex execution, one-off STA tasks, a static STA scheduler, `StaYield`, and generic task timeout helpers.
- Target `netstandard2.0`, `net8.0-windows`, `net9.0-windows`, and `net10.0-windows`.

[5.0.0]: https://github.com/AdaskoTheBeAsT/AdaskoTheBeAsT.Interop.Threading/compare/v4.0.1...v5.0.0
[4.0.1]: https://github.com/AdaskoTheBeAsT/AdaskoTheBeAsT.Interop.Threading/compare/v4.0.0...v4.0.1
[4.0.0]: https://github.com/AdaskoTheBeAsT/AdaskoTheBeAsT.Interop.Threading/compare/v3.1.0...v4.0.0
[3.1.0]: https://github.com/AdaskoTheBeAsT/AdaskoTheBeAsT.Interop.Threading/compare/v3.0.0...v3.1.0
[3.0.0]: https://github.com/AdaskoTheBeAsT/AdaskoTheBeAsT.Interop.Threading/compare/v2.1.0...v3.0.0
[2.1.0]: https://github.com/AdaskoTheBeAsT/AdaskoTheBeAsT.Interop.Threading/compare/v2.0.0...v2.1.0
[2.0.0]: https://github.com/AdaskoTheBeAsT/AdaskoTheBeAsT.Interop.Threading/compare/v1.0.0...v2.0.0
[1.0.0]: https://github.com/AdaskoTheBeAsT/AdaskoTheBeAsT.Interop.Threading/tree/v1.0.0
