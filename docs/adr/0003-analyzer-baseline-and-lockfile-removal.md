# ADR 0003: Analyzer baseline, native pointer types, and lockfile removal

- Status: Accepted
- Date: 2026-10-24

## Context

Following the 4.0.0 release, routine dependency and analyzer upgrades exposed three maintenance issues across the repository:

1. **Analyzer upgrades (`StyleCop.Analyzers` 1.3.x and `Microsoft.VisualStudio.Threading.Analyzers` 18.12.x)**:
   - `SA1121` began flagging explicit `IntPtr` and `UIntPtr` type names across P/Invoke declarations and native message-loop code, preferring C# `nint` and `nuint` aliases across all target frameworks.
   - `VSTHRD003` flagged `TaskCompletionSource.Task` properties (`SingleThreadedApartmentTaskScheduler.Completion` and `StaWorkItem<T>.Task`) that intentionally expose lifecycle or queued-work completion tasks created outside the caller's await context.
   - `VSTHRD002` flagged `.GetAwaiter().GetResult()` inside the result-extraction callbacks passed to `TaskWait.StartAsync`, even though `TaskWait.Transfer` invokes those callbacks only after `Task.WhenAny` confirms the source task has already completed.
2. **NuGet lockfiles (`packages.lock.json` and `RestorePackagesWithLockFile`)**:
   - Maintaining committed `packages.lock.json` files across `src`, `test`, `samples`, and `benchmarks` caused `NU1004` restore failures whenever shared build-time analyzer packages in `Directory.Build.props` were updated, while providing no downstream guarantee to NuGet package consumers (who resolve the library's open dependency version ranges against their own graphs).
3. **Test project target conditions and OS guards**:
   - `test/Directory.Build.props` still listed dropped `.NET Framework` 4.6.2/4.7/4.7.1 monikers in its condition, and several Windows-only unit test files retained redundant runtime `SkipIfNotWindows()` helpers even though non-Windows builds already exclude those files at compile time in `AdaskoTheBeAsT.Interop.Threading.Test.csproj`.

## Decision

1. **Select patch version `4.0.1`**:
   - Because no public types, members, or behavioral contracts were added or broken—and the runtime dependency update (`System.Threading.AccessControl` `[10.0.13,11.0.0)` and `[9.0.21,10.0.0)`) is a patch-level bump—increment the package version from `4.0.0` to `4.0.1` in accordance with Semantic Versioning.
2. **Disable `SA1121` in `AdaskoTheBeAsT.ruleset` and use targeted `#pragma warning disable` for `VSTHRD002`/`VSTHRD003`**:
   - Disable `SA1121` in the shared ruleset so Win32 P/Invoke signatures (`HWND`, `WPARAM`, `LPARAM`, `HANDLE`) can continue using explicit `IntPtr` and `UIntPtr` types consistently across `.NET Framework` and modern `.NET` targets.
   - Keep `VSTHRD002` and `VSTHRD003` enabled globally so genuine synchronous-wait hazards and foreign-task awaits are caught, and apply narrow `#pragma warning disable` / `#pragma warning restore` blocks around the two `TaskCompletionSource.Task` properties and the two post-completion result extractors.
3. **Remove `RestorePackagesWithLockFile` and `packages.lock.json` files**:
   - Remove `<RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>` from `Directory.Build.props`, delete the remaining `packages.lock.json` files, and run `dotnet restore` without `--locked-mode` in `scripts/Invoke-Validation.ps1`.
4. **Align test build conditions and Windows-only test fixtures**:
   - Use `Condition="$(TargetFramework.StartsWith('net4'))"` in `test/Directory.Build.props` to match the root `Directory.Build.props`.
   - Remove redundant `SkipIfNotWindows()` / `IsWindows()` methods from Windows-only test classes that are already guarded by compile-time file exclusion on non-Windows platforms and `[SupportedOSPlatform("windows")]`.

## Consequences

Positive:

- Zero warnings and zero errors with `TreatWarningsAsErrors=true` on the latest analyzer packages across all six target frameworks (`net10.0`, `net9.0`, `net8.0`, `net481`, `net48`, `net472`), samples, and benchmarks.
- Analyzer and dependency updates in `Directory.Build.props` apply cleanly across `src`, `test`, `samples`, and `benchmarks` without lockfile drift or `NU1004` failures.
- Explicit `IntPtr`/`UIntPtr` Win32 interop signatures remain readable and uniform across all target frameworks while threading analyzers remain active for the rest of the codebase.

Trade-offs:

- Builds and CI restores resolve current floating analyzer/reference inputs directly from `Directory.Build.props` rather than freezing transitive development graphs in `packages.lock.json`.
