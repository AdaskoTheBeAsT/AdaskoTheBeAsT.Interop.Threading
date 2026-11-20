# ADR 0004: .NET 11 target baseline, removed frameworks, and major version 5.0.0

- Status: Accepted
- Date: 2026-11-16

## Context

Aligning `AdaskoTheBeAsT.Interop.Threading` with the `.NET 11` ecosystem and the 2026 STA interop hardening work required coordinated updates to target frameworks, SDK/compiler baselines, dependencies, and package versioning:

1. **Target framework lifecycle**:
   - `.NET 11` (`net11.0`) is now available with SDK `11.0.100` and C# 14 (`LangVersion` `14.0`).
   - Retaining `net8.0` and `net9.0` alongside `net10.0`, `net11.0`, and three `.NET Framework` targets (`net472`, `net48`, `net481`) would expand the build and validation matrix to seven targets across older modern `.NET` releases that downstream `AdaskoTheBeAsT.Interop.*` packages are retiring in lockstep.
2. **Semantic Versioning impact**:
   - Removing `net8.0` and `net9.0` package assets is a breaking package-target change for consumers still pinned to those frameworks.
   - Enabling native COM STA apartment verification by default (`VerifyNativeStaApartment = true`, see [ADR 0005](0005-native-sta-verification-and-thread-scope-lifecycle.md)) intentionally fails scheduler construction when a worker thread lands in native MTA instead of silently proceeding.
3. **Sample and benchmark project targeting**:
   - Sample executables (`StaService` and `MutexProbe`) can move directly to `net11.0-windows`.
   - `BenchmarkDotNet` `0.15.8` (the latest published NuGet version) predates `RuntimeMoniker.Net11_0` and throws `NotImplementedException` inside `RuntimeMonikerExtensions.GetRuntimeVersion(NotRecognized)` when validating out-of-process jobs on `net11.0-windows`.
4. **Deterministic CI builds**:
   - `AdaskoTheBeAsT.Interop.Threading.csproj` previously checked only `TF_BUILD` when enabling `ContinuousIntegrationBuild`, whereas repository builds run on GitHub Actions (`GITHUB_ACTIONS=true`).

## Decision

1. **Increment the package version to `5.0.0`**:
   - Publish these changes as major version `5.0.0` under Semantic Versioning to reflect the removed `net8.0`/`net9.0` targets and stricter default native STA apartment verification.
   - Keep `ISingleThreadedApartmentTaskScheduler` and `ICooperativeStaTaskScheduler` binary- and source-compatible for supported frameworks.
2. **Ship five target frameworks (`net11.0`, `net10.0`, `net481`, `net48`, `net472`)**:
   - Add `net11.0`, retain `net10.0` and `.NET Framework` `4.7.2`/`4.8`/`4.8.1`, and remove `net9.0` and `net8.0` from `src/` and `test/`.
   - Select SDK `11.0.100` in `global.json`, set `LangVersion` to `14.0` in `Directory.Build.props`, update `System.Threading.AccessControl` to `[11.0.0,12.0.0)` (for `net11.0` and `.NET Framework`) and `[10.0.15,11.0.0)` (for `net10.0`), and upgrade Roslyn/Meziantou/Sonar/StyleCop analyzers and `xunit.v3` (`4.0.3`).
3. **Target `net11.0-windows` in samples and retain `net10.0-windows` in `benchmarks/Threading.Benchmarks`**:
   - Retarget `samples/StaService`, `samples/MutexProbe`, and `scripts/Test-MutexProcesses.ps1` to `net11.0-windows`.
   - Keep the standalone `benchmarks/Threading.Benchmarks` harness on supported `net10.0-windows` (built under SDK `11.0.100`) until `BenchmarkDotNet` ships native `RuntimeMoniker.Net11_0` support, preserving full `CsProjCoreToolchain` and `ThreadingDiagnoser` execution without custom toolchain workarounds.
4. **Enable deterministic CI metadata on GitHub Actions**:
   - Enable `ContinuousIntegrationBuild` when either `TF_BUILD` or `GITHUB_ACTIONS` is `"true"`.

## Why

- Shipping `net11.0` and `net10.0` alongside `net472`/`net48`/`net481` covers current STS/LTS modern `.NET` runtimes and Windows `.NET Framework` hosts while keeping the multi-target test matrix focused.
- Bumping to `5.0.0` communicates target removal and constructor apartment-verification enforcement clearly to NuGet consumers.
- Keeping the non-shipping benchmark harness on `net10.0-windows` avoids brittle reflection or in-process diagnoser compromises while exercising the current library source under SDK `11.0.100`.

## Consequences

Positive:

- Clean zero-warning builds across all five shipping targets, samples, and benchmarks with C# 14 and the `.NET 11` analyzer stack.
- Portable and Windows validation lanes (`scripts/Invoke-Validation.ps1`) exercise both `net11.0` and `net10.0` (including 32-bit `win-x86` when 32-bit CoreCLR runtimes are installed) alongside `.NET Framework`.
- Deterministic symbol and Source Link paths are active automatically in GitHub Actions builds.

Trade-offs:

- Applications still targeting `net8.0` or `net9.0` must retarget to `net10.0` or `net11.0` before upgrading to `5.0.0`, or remain on `4.0.1`.
- Out-of-process `BenchmarkDotNet` runs measure the `net10.0` build until an upstream `BenchmarkDotNet` release adds `.NET 11` runtime moniker recognition.
