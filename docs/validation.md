# Reproducible validation

## Current 4.0 configuration

The working tree now defaults to `Version=4.0.0` and targets **six frameworks**:
`net10.0`, `net9.0`, `net8.0`, `net481`, `net48`, and `net472`.
`global.json` selects SDK **10.0.401** and the Microsoft.Testing.Platform runner.
See the [migration guide](../MIGRATION.md) for the removed targets and behavior
changes.

The harness now uses the six supported targets on x64 and `net472` on x86,
with Microsoft.Testing.Platform arguments and xUnit TRX reports.
**The historical evidence below does not validate a 4.0 release.** In particular:

- The current build files do not enable the earlier SDK package/API validation
  against 3.1.0. Do not infer API compatibility from those historical results.
- Lockfiles must match the current framework and dependency inputs before a
  locked restore can pass.

For the current library, start with a locked restore and build from the repository
root:

```powershell
$library = 'src/AdaskoTheBeAsT.Interop.Threading/AdaskoTheBeAsT.Interop.Threading.csproj'
dotnet restore $library --locked-mode
dotnet build $library -c Release --no-restore -p:ContinuousIntegrationBuild=true
```

Check each exit code before continuing. A failed locked restore requires review
of the dependency changes, not silently bypassing locked mode. Before release,
rerun the six-target matrix and the supported x86 target, and record fresh
package/API compatibility evidence.

### PR #4 review-fix checks, 2026-09-10

Checked locally on Windows with SDK 10.0.401:

- Locked solution restore and six-target Release build passed with zero warnings
  or errors.
- Windows mode passed all 133 tests on each of the six x64 targets (798 test
  executions), then stopped at the `StaService` locked restore with `NU1004`.
  The sample lockfile still requests older shared analyzer dependencies
  (`AdaskoTheBeAsT.AsyncFixer` 2.1.0.200 instead of 2.1.0.250). Sample builds,
  hosted-service execution, and the child-process mutex harness were not reached.
- X86 mode passed all 133 tests on `net472`, including the pointer-size assertion,
  using `--arch x86`.
- Portable mode passed all 17 timeout tests on Windows. This is not Linux
  execution evidence.
- Focused STA yield/contract tests passed 174 executions across the six targets.
  The two new negative-duration cases first reproduced the incorrect `"checkEveryMs"`
  parameter name, then passed with `"ms"`.

The sample lockfile mismatch remains outside these two review fixes. No lockfiles
were regenerated or locked-restore checks bypassed.

### Documentation checks, 2026-09-10

The documentation update was checked locally on Windows with SDK 10.0.401:

- Locked library restore and six-target Release build passed, with zero warnings
  or errors.
- All 16 current README/migration C# examples compiled against the built library
  on `net8.0-windows` and `net10.0-windows`. Imports and application placeholders
  were supplied where needed; the explicitly historical 2.x example was excluded.
  Examples were compiled, not executed.
- Local links, repository-file links, heading anchors, and documented framework
  and version values passed structural checks. External URLs were not fetched.
- The generated 4.0.0 package contained the updated README and six DLL assets.

The unit-test matrix, x86 execution, and package/API comparison were not rerun for
this documentation-only change. These checks do not replace release validation.

The remaining sections retain the earlier procedures and measurements for
reference. Their commands and results are **historical**, not current validation
instructions or newly verified outcomes.

## Historical nine-target procedure

Use PowerShell 7, the SDK selected by `global.json`, and the .NET 8, 9, and 10
runtimes. Windows validation also requires the installed .NET Framework 4.x
runtime. Reference assemblies come from the locked NuGet graph on every OS.

Run from the repository root:

```powershell
./scripts/Invoke-Validation.ps1 -Mode Windows
./scripts/Invoke-Validation.ps1 -Mode X86
# On Linux, with PowerShell 7 and .NET installed:
./scripts/Invoke-Validation.ps1 -Mode Portable
```

The script checks every child-process exit code and enforces finite deadlines.
A harness deadline can kill the test process, not safely stop native code inside
a production application. Do not run the modes concurrently in one working tree.

| Mode | Checks |
| --- | --- |
| Windows | Locked restore; Release build with warnings-as-errors and package validation; all nine TFMs on x64; hosted-service startup/shutdown cases; child-process mutex harness |
| X86 | Entire `net462` suite in an explicitly requested x86 testhost, including native dispatch/quit tests and a pointer-size assertion |
| Portable | `net10.0` timeout tests; on non-Windows the project excludes all native test files |

`net481`, `net48`, `net472`, `net471`, `net47`, and `net462` all execute on the
installed Framework 4.x runtime, which is an in-place runtime family. This is not
evidence of execution on six historical Framework installations.

## Historical CI and release boundary

`.github/workflows/validation.yml` defines these lanes explicitly, without
publishing credentials and with read-only repository permission. It also compiles
the benchmarks. A workflow file is not evidence of a successful remote run.

The existing `ci.yml` remains the separate reusable build/Sonar/NuGet workflow.
Its inspected `@v1` implementation builds Debug, collects test coverage, and
builds/publishes Release on version tags. It takes its runner/SDK settings from
repository variables. Do not infer its matrix from the caller file alone.
The mutable `@v1` reference and inherited publishing-secret scope remain release
maintenance concerns. Pinning/upgrading that external workflow and adjusting
its secret boundary need a reviewed upstream revision; this change does not
claim to have hardened or deployed it.

## Historical restore, versions, and API compatibility

Commit `packages.lock.json` for the library, tests, samples, and benchmarks.
Validation uses `dotnet restore --locked-mode`. For an intentional dependency
update, change the project references, run ordinary restore for every affected
project, review lockfile diffs, and rerun locked validation.

The library retains its consumer-facing AccessControl version ranges. The
lockfiles freeze development inputs, not downstream consumers' dependency
resolution. Framework reference-assembly and analyzer packages are private.
On `net10.0`, the SDK prunes the AccessControl dependency because that assembly
is supplied by the shared framework. The other package groups retain it.

At the time of these checks, the local next-release default was
`VersionPrefix=3.2.0`; CI could override
`Version` or `VersionPrefix`. Assembly/file versions are SDK-derived, not separately
hard-coded. SDK package validation compares all nine assets with released
**3.1.0**, preserving the original scheduler interface.

```powershell
$library = 'src/AdaskoTheBeAsT.Interop.Threading/AdaskoTheBeAsT.Interop.Threading.csproj'
dotnet restore $library --locked-mode
dotnet build $library -c Release --no-restore -p:ContinuousIntegrationBuild=true
dotnet pack $library -c Release --no-build -p:ContinuousIntegrationBuild=true
```

**Build before packing.** With `GeneratePackageOnBuild=true`, a standalone pack
can reuse old DLLs. A new package filename does not prove its assemblies were
rebuilt. Apply any CI version override consistently to build and pack.

Inspect the `.nupkg` and `.snupkg` in `src/.../bin/Release`: nine DLL/XML/PDB asset
sets, README, LICENSE, dependency groups, and portable PDBs. Source Link should
map to the intended commit. Source stepping needs a clean committed release:
locally modified tracked files cannot be reproduced from their old commit URL.

The old `DeterministicBuild.targets` workaround remains. Repeated same-path
builds are useful evidence, but are not a substitute for clean-checkout builds
at different paths and source-debugging checks before removing it.

## Historical analyzer and dependency review

The existing analyzer set is retained, with warnings-as-errors. Several explicitly
referenced `Microsoft.CodeAnalysis.*` packages also arrive transitively through
the Roslyn metapackage. Likewise, explicit NetAnalyzers overlap SDK capabilities.
Removing them without comparing enabled diagnostics and cold/warm build times
would not establish an improvement. All remain private, excluded from runtime
assets. Modern xUnit v3 and Framework xUnit v2 remain deliberately separate;
their AutoFixture integration versions follow those two test stacks.

## Historical focused checks, coverage, and performance

```powershell
$tests = 'test/unit/AdaskoTheBeAsT.Interop.Threading.Test/AdaskoTheBeAsT.Interop.Threading.Test.csproj'
dotnet test $tests -c Release -f net10.0 --filter 'FullyQualifiedName~TaskExtension'
dotnet test $tests -c Release -f net462 --filter 'FullyQualifiedName~Sta'

dotnet-coverage collect "dotnet test $tests -c Release -f net10.0 --no-build" `
  -s coverage.settings.xml -f xml -o artifacts/coverage/coverage.net10.xml
dotnet-coverage collect "dotnet test $tests -c Release -f net462 --no-build" `
  -s coverage.settings.xml -f xml -o artifacts/coverage/coverage.net462.xml
```

Keep modern and legacy reports separate. Unit coverage does not include mutex
child-process execution, and neither line coverage nor a passing stress run
proves race freedom. See [benchmarks](../benchmarks/README.md) for measurement
commands and the difference between smoke checks and performance evidence.

## Historical local evidence, 2026-09-09

Working tree based on `601cf20`, including the preserved pre-existing edits.
These measurements predate the six-target 4.0 configuration and are not evidence
that the current tree passes those checks.
Windows 11 build 26200; SDK 10.0.303 selected under `global.json`'s patch
roll-forward policy; modern runtimes 10.0.12, 9.0.20, and 8.0.31.

| Check | Result |
| --- | --- |
| Release solution build and locked restores | Passed, zero warnings/errors |
| Nine-TFM x64 test matrix | 131/131 per TFM, 1,179 executions, no failures/skips |
| `net462` x86 testhost, asserted pointer size | 131/131, no failures/skips |
| Repeated lifecycle/queue subset | 14 tests repeated ten times, all passed |
| Hosted-service and two-process mutex scenarios | Passed |
| SDK API/package comparison with 3.1.0 | Passed across nine assets |
| Two Release rebuilds at the same path | All nine DLL/PDB pairs had identical SHA-256 hashes |
| Package contents and Source Link metadata | Nine DLL/XML/PDB sets, README, LICENSE, dependency groups, commit source mapping verified |
| Benchmark build and Dry execution | Zero build warnings/errors, all seven scenarios executed |

Fresh `dotnet-coverage` 18.3.2 reports for `AdaskoTheBeAsT.Interop.Threading.dll`:

| Report, under `artifacts/coverage/` | Line coverage | Block coverage |
| --- | --- | --- |
| `coverage.net10.xml` | 89.26% | 91.10% |
| `coverage.net462.xml` | 89.47% | 91.62% |

Both instrumented runs passed 131 tests. The final legacy collection used the
x86-built test assembly. Each report contains 24 partially covered and 41
uncovered lines; diagnostics emission is covered. Remaining paths include
throwing cancellation callbacks, exceptional scheduler teardown, unexpected
native wait results, and mutex branches not reached by the unit suite.
These reports were inspected locally without HTML translation or Sonar upload;
cloud analysis is not refreshed by these checks.

## Environment-dependent checks

- Run the new Linux CI lane with a Linux SDK. Local WSL exists but has no `dotnet`.
- Provision separate principals/sessions for restricted ACL integration checks.
  The local process harness does not prove cross-user or cross-session security.
- Validate real third-party COM activation/release separately from the fake service.
- Record clean-checkout source stepping and cross-path deterministic-build evidence.
- Record statistically useful before/after benchmarks before making speed or
  allocation improvement claims. No performance threshold is asserted here.
