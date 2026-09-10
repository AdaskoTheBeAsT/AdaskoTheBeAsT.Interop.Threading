# Threading measurements

Windows, .NET 10, BenchmarkDotNet 0.15.8. The benchmark project stays outside the
shipping solution/package. Build it with the same analyzer policy as the library:

```powershell
$project = 'benchmarks/Threading.Benchmarks/Threading.Benchmarks.csproj'
dotnet restore $project --locked-mode
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

## Smoke-run record, 2026-09-09

All seven Dry scenarios executed successfully on the working tree based on
`601cf20`: Windows 11 10.0.26200.9445, Intel Core i9-13980HX (24 physical/32 logical
cores), SDK 10.0.303, .NET 10.0.12 x64, BenchmarkDotNet 0.15.8. Outputs are under
`artifacts/benchmarks/dry/`. Expected minimum-iteration-time warnings make these
single-sample cold-start measurements unsuitable as a performance baseline.
The separate latency probe also completed; no before/after performance baseline
has been established.
