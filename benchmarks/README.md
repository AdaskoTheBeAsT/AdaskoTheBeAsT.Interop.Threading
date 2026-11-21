# Threading measurements

Windows, SDK 11.0.100 (`net10.0-windows` target), BenchmarkDotNet 0.15.8.
`BenchmarkDotNet 0.15.8` predates `RuntimeMoniker.Net11_0`, so the standalone
benchmark project targets supported `net10.0-windows` while building under the
repository's `.NET 11` SDK and analyzer policy. It stays outside the shipping
solution/package:

```powershell
$project = 'benchmarks/Threading.Benchmarks/Threading.Benchmarks.csproj'
dotnet restore $project
dotnet build $project -c Release --no-restore
# Smoke check only, not statistically useful performance results:
dotnet run --project $project -c Release --no-build -- --job Dry --filter '*'
# Full measurements, preferably on a quiet dedicated host:
dotnet run --project $project -c Release --no-build -- --filter '*' --exporters json
# A separate 1,000-item queue/execution latency probe:
dotnet run --project $project -c Release --no-build -- --latency
```

Scenarios cover a one-off OLE STA versus a reused scheduler, 32-item queue bursts,
pre-canceled admission, completed and expired timeout helpers, and queued work
under continuous native window-message traffic. Burst results are normalized
per item. The message benchmark reuses the dependency-free test window fixture.

MemoryDiagnoser reports managed allocations; ThreadingDiagnoser reports thread
pool activity for scheduling. BenchmarkDotNet's mean is elapsed time per operation,
whose reciprocal is throughput, not isolated delegate CPU time. These asynchronous
measurements include coordination costs. They do not measure all native allocations.

The latency probe reports submission-to-start and delegate-execution p50/p95
separately using `Stopwatch` timestamps. Submission includes admission overhead.
The workload is `SpinWait(100)`, not a claim about COM performance; its behavior
depends on the CPU/runtime. It is a diagnostic snapshot, not a regression threshold.

Keep BenchmarkDotNet's environment header, raw measurements, runtime, architecture,
SDK, OS, CPU, workload parameters, and commit with any recorded baseline. Compare
the same scenario/settings on a quiet host before and after an optimization.
Dry jobs validate scenario execution only. No speedup or allocation-reduction
claim follows from the presence of these benchmarks.

`benchmarks/Directory.Build.props` keeps Roslyn runtime assets available for
BenchmarkDotNet and its generated harness. The shipping library still excludes
these analyzer runtime assets.

## Smoke-run record, 2026-11-16

All seven Dry scenarios and the `--latency` probe executed with zero build
warnings or errors on the `5.0.0` working tree: Windows 11 `10.0.26300.9974`,
Intel Core i9-13980HX (24 physical/32 logical cores), SDK `11.0.100`,
`.NET 10.0.15` x64, BenchmarkDotNet `0.15.8`. Dry jobs validate scenario
execution only and do not establish a performance baseline.

## Historical smoke-run record, 2026-09-09

All seven Dry scenarios executed successfully on the working tree based on
`601cf20`: Windows 11 10.0.26200.9445, Intel Core i9-13980HX (24 physical/32 logical
cores), SDK 10.0.303, .NET 10.0.12 x64, BenchmarkDotNet 0.15.8. Outputs are under
`artifacts/benchmarks/dry/`. Expected minimum-iteration-time warnings make these
single-sample cold-start measurements unsuitable as a performance baseline.
The separate latency probe also completed; no before/after performance baseline
has been established.
