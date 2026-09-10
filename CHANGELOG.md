# Changelog

Notable changes to `AdaskoTheBeAsT.Interop.Threading`.

Entries before 4.0.0 are reconstructed from local Git tags and their source diffs; dates shown are tag commit dates, not verified NuGet publication dates.

See the [migration guide](MIGRATION.md) for upgrade steps.

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

[4.0.0]: https://github.com/AdaskoTheBeAsT/AdaskoTheBeAsT.Interop.Threading/compare/v3.1.0...v4.0.0
[3.1.0]: https://github.com/AdaskoTheBeAsT/AdaskoTheBeAsT.Interop.Threading/compare/v3.0.0...v3.1.0
[3.0.0]: https://github.com/AdaskoTheBeAsT/AdaskoTheBeAsT.Interop.Threading/compare/v2.1.0...v3.0.0
[2.1.0]: https://github.com/AdaskoTheBeAsT/AdaskoTheBeAsT.Interop.Threading/compare/v2.0.0...v2.1.0
[2.0.0]: https://github.com/AdaskoTheBeAsT/AdaskoTheBeAsT.Interop.Threading/compare/v1.0.0...v2.0.0
[1.0.0]: https://github.com/AdaskoTheBeAsT/AdaskoTheBeAsT.Interop.Threading/tree/v1.0.0
