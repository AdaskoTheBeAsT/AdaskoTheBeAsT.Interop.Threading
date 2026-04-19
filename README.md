# 🧵 AdaskoTheBeAsT.Interop.Threading

> 🪟 A friendly, production-ready Windows threading toolbox for cross-process mutexes, STA/COM work, and task timeouts — with a message pump that *actually* pumps. 💨

[![NuGet](https://img.shields.io/nuget/v/AdaskoTheBeAsT.Interop.Threading.svg?label=AdaskoTheBeAsT.Interop.Threading&logo=nuget)](https://www.nuget.org/packages/AdaskoTheBeAsT.Interop.Threading/)
[![NuGet downloads](https://img.shields.io/nuget/dt/AdaskoTheBeAsT.Interop.Threading.svg?logo=nuget&label=downloads)](https://www.nuget.org/packages/AdaskoTheBeAsT.Interop.Threading/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](./LICENSE)
![TFMs](https://img.shields.io/badge/TFMs-net10.0--windows%20%7C%20net9.0--windows%20%7C%20net8.0--windows%20%7C%20net4.6.2%E2%80%93net4.8.1-512BD4?logo=dotnet)
![Platform](https://img.shields.io/badge/platform-Windows-0078D6?logo=windows)
![Warnings](https://img.shields.io/badge/warnings--as--errors-on-green)
![Deterministic](https://img.shields.io/badge/deterministic%20build-on-blue)
[![CI](https://img.shields.io/github/actions/workflow/status/AdaskoTheBeAsT/AdaskoTheBeAsT.Interop.Threading/ci.yml?branch=main&logo=github&label=CI)](https://github.com/AdaskoTheBeAsT/AdaskoTheBeAsT.Interop.Threading/actions)

### 🔬 Code quality — SonarCloud

[![Quality Gate Status](https://sonarcloud.io/api/project_badges/measure?project=AdaskoTheBeAsT_AdaskoTheBeAsT.Interop.Threading&metric=alert_status)](https://sonarcloud.io/summary/new_code?id=AdaskoTheBeAsT_AdaskoTheBeAsT.Interop.Threading)
[![Coverage](https://sonarcloud.io/api/project_badges/measure?project=AdaskoTheBeAsT_AdaskoTheBeAsT.Interop.Threading&metric=coverage)](https://sonarcloud.io/component_measures?id=AdaskoTheBeAsT_AdaskoTheBeAsT.Interop.Threading&metric=coverage)
[![Maintainability Rating](https://sonarcloud.io/api/project_badges/measure?project=AdaskoTheBeAsT_AdaskoTheBeAsT.Interop.Threading&metric=sqale_rating)](https://sonarcloud.io/component_measures?id=AdaskoTheBeAsT_AdaskoTheBeAsT.Interop.Threading&metric=sqale_rating)
[![Reliability Rating](https://sonarcloud.io/api/project_badges/measure?project=AdaskoTheBeAsT_AdaskoTheBeAsT.Interop.Threading&metric=reliability_rating)](https://sonarcloud.io/component_measures?id=AdaskoTheBeAsT_AdaskoTheBeAsT.Interop.Threading&metric=reliability_rating)
[![Security Rating](https://sonarcloud.io/api/project_badges/measure?project=AdaskoTheBeAsT_AdaskoTheBeAsT.Interop.Threading&metric=security_rating)](https://sonarcloud.io/component_measures?id=AdaskoTheBeAsT_AdaskoTheBeAsT.Interop.Threading&metric=security_rating)
[![Bugs](https://sonarcloud.io/api/project_badges/measure?project=AdaskoTheBeAsT_AdaskoTheBeAsT.Interop.Threading&metric=bugs)](https://sonarcloud.io/component_measures?id=AdaskoTheBeAsT_AdaskoTheBeAsT.Interop.Threading&metric=bugs)
[![Vulnerabilities](https://sonarcloud.io/api/project_badges/measure?project=AdaskoTheBeAsT_AdaskoTheBeAsT.Interop.Threading&metric=vulnerabilities)](https://sonarcloud.io/component_measures?id=AdaskoTheBeAsT_AdaskoTheBeAsT.Interop.Threading&metric=vulnerabilities)
[![Code Smells](https://sonarcloud.io/api/project_badges/measure?project=AdaskoTheBeAsT_AdaskoTheBeAsT.Interop.Threading&metric=code_smells)](https://sonarcloud.io/component_measures?id=AdaskoTheBeAsT_AdaskoTheBeAsT.Interop.Threading&metric=code_smells)
[![Duplicated Lines (%)](https://sonarcloud.io/api/project_badges/measure?project=AdaskoTheBeAsT_AdaskoTheBeAsT.Interop.Threading&metric=duplicated_lines_density)](https://sonarcloud.io/component_measures?id=AdaskoTheBeAsT_AdaskoTheBeAsT.Interop.Threading&metric=duplicated_lines_density)
[![Technical Debt](https://sonarcloud.io/api/project_badges/measure?project=AdaskoTheBeAsT_AdaskoTheBeAsT.Interop.Threading&metric=sqale_index)](https://sonarcloud.io/component_measures?id=AdaskoTheBeAsT_AdaskoTheBeAsT.Interop.Threading&metric=sqale_index)
[![Lines of Code](https://sonarcloud.io/api/project_badges/measure?project=AdaskoTheBeAsT_AdaskoTheBeAsT.Interop.Threading&metric=ncloc)](https://sonarcloud.io/component_measures?id=AdaskoTheBeAsT_AdaskoTheBeAsT.Interop.Threading&metric=ncloc)

---

## 👋 Hello, threading friend

Native-on-Windows code is fun, right up until it *isn't*. You know the signs:

- 🏢 a COM component that quietly insists on **STA** + message pumping
- 🔒 a resource that must be **serialized across processes** (not just threads)
- ⏳ a call that might never come back, so you need a real **timeout** — one that respects cancellation tokens
- 🧟 a mutex left behind by a process that crashed, waiting to ambush the next caller
- 🧪 a scheduler that must behave in unit tests: **disposable, injectable, mockable**

`AdaskoTheBeAsT.Interop.Threading` is the reusable boilerplate you keep rewriting in every project: named cross-process mutexes with sensible ACLs, a dedicated STA thread with a real OLE message loop, and task timeouts that distinguish *"I gave up"* from *"the caller canceled me"*. 📦

And now it's a library. ✨

---

## ✨ Why you'll love this

- 🧵 **Instance-based STA scheduler.** `SingleThreadedApartmentTaskScheduler` owns its own STA thread, implements `ISingleThreadedApartmentTaskScheduler` + `IDisposable`, and shuts down **deterministically**. No more static global state.
- 🧩 **DI-friendly options.** `SingleThreadedApartmentTaskSchedulerOptions` binds to `Microsoft.Extensions.Options` out of the box.
- ⏱️ **Per-item timeouts.** `RunAsync(func, timeout, ct)` overload plus `DefaultWorkItemTimeout` on options.
- 🕊️ **Cooperative cancellation that actually composes.** Caller token ⨯ scheduler-shutdown token, observed pre- and post-execution by `StaWorkItem`.
- 🧮 **Full-precision timing.** `StaYield` uses `Stopwatch.GetTimestamp()` with full-precision ms → tick math — no `Environment.TickCount` wraparound surprises.
- 🔒 **Cross-process mutexes done right.** Global `\Global\` prefix, cached `MutexSecurity`, reflection-resolved `SetAccessControl` (works on net4x **and** net8+), abandoned-mutex recovery.
- 🪟 **9 TFMs, all green.** `net10.0-windows`, `net9.0-windows`, `net8.0-windows`, `net481`, `net48`, `net472`, `net471`, `net47`, `net462` — the full matrix on every build.
- 🔎 **Source Link + `snupkg`.** Step into the library from your debugger without guessing.
- 🛡️ **Warnings-as-errors + deterministic builds.** Because future-you deserves reproducibility.
- 📐 **Tiny public surface.** Seven public types: `MutexHelper`, `SingleThreadedApartmentTask`, `SingleThreadedApartmentTaskScheduler`, `ISingleThreadedApartmentTaskScheduler`, `SingleThreadedApartmentTaskSchedulerOptions`, `StaYield`, `TaskExtension`.

---

## 📦 Install

```bash
dotnet add package AdaskoTheBeAsT.Interop.Threading
```

Or via the NuGet Package Manager console:

```powershell
Install-Package AdaskoTheBeAsT.Interop.Threading
```

Symbols ship as `.snupkg` with Source Link and embedded untracked sources — step in, look around, it's fine.

---

## 📚 Table of contents

- [👋 Hello, threading friend](#-hello-threading-friend)
- [✨ Why you'll love this](#-why-youll-love-this)
- [📦 Install](#-install)
- [🗺️ Target framework matrix](#️-target-framework-matrix)
- [💡 The core idea](#-the-core-idea)
- [🎯 Key features](#-key-features)
  - [MutexHelper — cross-process synchronization](#1-mutexhelper---cross-process-synchronization)
  - [SingleThreadedApartmentTask — STA execution](#2-singlethreadedapartmenttask---sta-execution)
  - [SingleThreadedApartmentTaskScheduler — reusable STA thread](#3-singlethreadedapartmenttaskscheduler---reusable-sta-thread)
  - [TaskExtension — task timeout management](#4-taskextension---task-timeout-management)
- [🔧 Advanced scenarios](#-advanced-scenarios)
- [🎓 Real-world examples](#-real-world-examples)
- [🏗️ Technical details](#️-technical-details)
- [🧭 Architecture decision records](#-architecture-decision-records)
- [⚠️ Known considerations](#️-known-considerations)
- [🔄 Migration guide (2.x → 3.0)](#-migration-guide)
- [📋 Changelog](#-changelog)
- [📝 License](#-license)
- [🤝 Contributing](#-contributing)

---

## 🗺️ Target framework matrix

| TFM | Status | Notes |
| --- | :-: | --- |
| `net10.0-windows` | ✅ | Primary target; `LibraryImport` source-generated P/Invoke. |
| `net9.0-windows` | ✅ | Primary target; `LibraryImport`. |
| `net8.0-windows` | ✅ | Primary target; `LibraryImport`. |
| `net481` | ✅ | Windows desktop; classic `DllImport`. |
| `net48` | ✅ | Same as above. |
| `net472` | ✅ | Same as above. |
| `net471` | ✅ | Same as above. |
| `net47` | ✅ | Same as above. |
| `net462` | ✅ | Minimum supported TFM. |

Every cell is built with `TreatWarningsAsErrors=true`, `ContinuousIntegrationBuild=true`, `Deterministic=true`, and exercised in CI.

---

## 💡 The core idea

Most Windows interop pain comes from four recurring themes. This library gives each one a tiny, focused primitive:

```
             ┌──────────────────────────────────────┐
             │  Caller (your app / test / service)  │
             └──────────────────────────────────────┘
                 │          │          │         │
                 │ Mutex    │ STA      │ Schedule │ Timeout
                 ▼          ▼          ▼         ▼
         ┌───────────┐ ┌─────────┐ ┌───────┐ ┌──────────────┐
         │MutexHelper│ │SingleThr│ │Scheduler│ │TaskExtension│
         │  🔒       │ │Apartment│ │  🧵     │ │    ⏱️       │
         │           │ │Task 🏢  │ │         │ │              │
         │Global\... │ │Ad-hoc   │ │Persistent│ │TimeoutAfter │
         │abandoned- │ │STA run  │ │STA queue │ │Async, CT-   │
         │mutex OK   │ │per call │ │ + pump  │ │aware        │
         └───────────┘ └─────────┘ └───────┘ └──────────────┘
                 │          │          │         │
                 ▼          ▼          ▼         ▼
             ┌──────────────────────────────────────┐
             │  Win32 / OLE / Message Pump (Windows)│
             └──────────────────────────────────────┘
```

Use one or all of them. They're independent, each < 200 LoC, each with a tight test matrix.

---

## 🎯 Key Features

### 1. MutexHelper - Cross-Process Synchronization
Safely run code blocks within a named mutex, ensuring exclusive execution across processes with proper security settings.

**Features:**
- Global or local mutex scope
- Configurable timeout with meaningful exceptions
- Automatic recovery from abandoned mutexes
- Security settings allowing cross-session access

**Basic Usage:**
```csharp
using AdaskoTheBeAsT.Interop.Threading;

// Simple global mutex
var result = MutexHelper.RunInMutex("MyAppInstance", () => {
    // Only one process can execute this at a time
    return PerformCriticalOperation();
});
```

**With Timeout:**
```csharp
// Timeout after 30 seconds if mutex can't be acquired
var result = MutexHelper.RunInMutex(
    "MyMutexName", 
    TimeSpan.FromSeconds(30), 
    () => {
        return ProcessSharedResource();
    });
```

**Local Mutex (non-global):**
```csharp
// Use local mutex for same-process synchronization
var result = MutexHelper.RunInMutex(
    "LocalMutex",
    TimeSpan.FromSeconds(10),
    isGlobal: false,
    () => DoWork());
```

### 2. SingleThreadedApartmentTask - STA Execution
Execute tasks in a Single-Threaded Apartment state, essential for COM interop, Windows clipboard operations, and legacy UI components.

**Features:**
- Full STA thread context with COM initialization
- Cancellation token support
- Exception propagation
- Message pump for COM interop
- Background thread execution (won't block process shutdown)

**Basic Usage:**
```csharp
// Execute code on STA thread
var result = await SingleThreadedApartmentTask.RunAsync(
    () => {
        // Code runs in STA apartment state
        // Perfect for COM objects, clipboard, etc.
        return GetDataFromStaComponent();
    },
    cancellationToken);
```

**With Timeout:**
```csharp
// Combine STA execution with timeout
var result = await SingleThreadedApartmentTask.RunWithTimeoutAsync(
    TimeSpan.FromSeconds(30),
    () => {
        return CallLegacyComComponent();
    },
    cancellationToken);
```

**With Message Pump (StaYield):**
```csharp
var result = await SingleThreadedApartmentTask.RunAsync(
    (StaYield staYield) => {
        for (int i = 0; i < 1000; i++) {
            DoWork(i);
            // Pump messages periodically to keep UI responsive
            staYield.Occasionally();
        }
        return result;
    },
    cancellationToken);
```

### 3. SingleThreadedApartmentTaskScheduler - Reusable STA Thread
Share a single STA thread across multiple tasks for better performance when you need frequent STA execution.

**Features:**
- Persistent STA thread with message loop
- OLE/COM initialization
- Queued task execution
- Cancellation support
- Instance-based (`IDisposable`) so each scheduler owns its own STA thread and can be deterministically shut down
- `ISingleThreadedApartmentTaskScheduler` interface for dependency injection and unit-testing

**Usage:**
```csharp
// Multiple tasks can share the same STA thread
using var scheduler = new SingleThreadedApartmentTaskScheduler();

var task1 = scheduler.RunAsync(() => ComOperation1(), cancellationToken);
var task2 = scheduler.RunAsync(() => ComOperation2(), cancellationToken);

await Task.WhenAll(task1, task2);
```

**With StaYield:**
```csharp
using var scheduler = new SingleThreadedApartmentTaskScheduler();

await scheduler.RunAsync((StaYield staYield) => {
    while (!condition) {
        // Wait for condition while pumping messages
        staYield.SpinUntil(() => CheckCondition(), checkEveryMs: 10);
    }

    // Or sleep without blocking the message pump
    staYield.Sleep(1000);
});
```

**Register as a singleton in DI:**
```csharp
builder.Services.AddSingleton<ISingleThreadedApartmentTaskScheduler>(
    _ => new SingleThreadedApartmentTaskScheduler(
        new SingleThreadedApartmentTaskSchedulerOptions { ThreadName = "App-STA" }));
```

Or, if you already use `Microsoft.Extensions.Options`, bind the options from configuration and resolve them in the factory:

```csharp
builder.Services.Configure<SingleThreadedApartmentTaskSchedulerOptions>(
    builder.Configuration.GetSection("StaScheduler"));
builder.Services.AddSingleton<ISingleThreadedApartmentTaskScheduler>(sp =>
    new SingleThreadedApartmentTaskScheduler(
        sp.GetRequiredService<IOptions<SingleThreadedApartmentTaskSchedulerOptions>>().Value));
```

### 4. TaskExtension - Task Timeout Management
Add timeout capabilities to any existing Task with proper cancellation token linking.

**Features:**
- Timeout exception with descriptive message
- Cancellation token propagation
- Async cancellation on .NET 8+
- ConfigureAwait(false) for library code

**Usage:**
```csharp
using AdaskoTheBeAsT.Interop.Threading;

// Add timeout to any task
var result = await LongRunningOperation()
    .TimeoutAfterAsync(TimeSpan.FromSeconds(30), cancellationToken);
```

**With Existing Task:**
```csharp
// Works with any Task<T>
var dataTask = FetchDataAsync();
try {
    var data = await dataTask.TimeoutAfterAsync(TimeSpan.FromSeconds(5), cancellationToken);
    ProcessData(data);
} catch (TimeoutException) {
    Logger.Warning("Operation timed out after 5 seconds");
}
```

## 🔧 Advanced Scenarios

### StaYield - Message Pump Control
When running long operations in STA threads, use `StaYield` to keep the message pump responsive.

```csharp
// Works identically with SingleThreadedApartmentTask or an instance of SingleThreadedApartmentTaskScheduler
var result = await SingleThreadedApartmentTask.RunAsync((StaYield staYield) => {
    var items = GetLargeItemList();
    
    foreach (var item in items) {
        ProcessItem(item);
        
        // Pump messages every 15ms (default) during long loops
        staYield.Occasionally();
    }
    
    // Wait for a condition without blocking messages
    staYield.SpinUntil(() => IsReady(), checkEveryMs: 10);
    
    // Sleep while keeping message pump active
    staYield.Sleep(1000);
    
    return GetResults();
}, cancellationToken);
```

### Handling Abandoned Mutexes
The library automatically handles abandoned mutexes (when a process crashes while holding the mutex):

```csharp
// If another process crashes while holding the mutex,
// this will log a warning and continue execution
var result = MutexHelper.RunInMutex("SharedResource", () => {
    // Your code here - the library handles recovery
    return DoWork();
});
```

## 🎓 Real-World Examples

### Single Instance Application
```csharp
public class Program {
    private const string MutexName = "MyApp_SingleInstance";
    
    public static void Main(string[] args) {
        try {
            MutexHelper.RunInMutex(MutexName, TimeSpan.Zero, () => {
                // Application code here
                RunApplication();
                return 0;
            });
        } catch (TimeoutException) {
            Console.WriteLine("Application is already running!");
            Environment.Exit(1);
        }
    }
}
```

### COM Interop with Timeout
```csharp
public async Task<string> GetClipboardTextAsync(CancellationToken ct) {
    return await SingleThreadedApartmentTask.RunWithTimeoutAsync(
        TimeSpan.FromSeconds(5),
        () => {
            // Clipboard operations require STA thread
            return Clipboard.GetText();
        },
        ct);
}
```

### Using with AdaskoTheBeAsT.Interop.COM
`AdaskoTheBeAsT.Interop.COM` handles registration-free COM activation, while this library provides the STA thread and scheduling model around those calls.

For a one-off COM calculation with timeout:

```csharp
var value = await SingleThreadedApartmentTask.RunWithTimeoutAsync(
    TimeSpan.FromSeconds(10),
    () =>
    {
        decimal result = default;
        var execution = Executor.Execute(comDllPath, manifestPath, () =>
        {
            var calculator = new LegacyCalculator.CalculatorClass();
            result = calculator.Add(left, right);
        });

        if (!execution.Success)
        {
            throw new InvalidOperationException("The COM calculation failed.", execution.Exception);
        }

        return result;
    },
    cancellationToken);
```

For repeated COM calculations that must stay serialized on one STA thread, create an instance of `SingleThreadedApartmentTaskScheduler` and reuse it (see the hosted-service example below).

See [Using AdaskoTheBeAsT.Interop.Threading with AdaskoTheBeAsT.Interop.COM](docs/using-with-adaskothebeast-interop-com.md) for a full guide.

### Hosted Service for serialized COM requests
If the COM server hangs when multiple callers invoke it in parallel, put a queue in front of a single STA worker.

The pattern is:

1. create the COM object once in `StartAsync` by calling `Executor.Create(...)` on the scheduler thread and keep the returned `ComObjectHandle<T>`;
2. accept incoming requests through a queue;
3. process each request through `SingleThreadedApartmentTaskScheduler.RunAsync(...)` so every call stays on the same STA thread and runs one-by-one.
4. release the handle in `StopAsync` by calling `Executor.Free(...)` on that same scheduler thread.

```csharp
using System.Threading.Channels;
using AdaskoTheBeAsT.Interop.COM;
using AdaskoTheBeAsT.Interop.Threading;
using Microsoft.Extensions.Hosting;

public sealed class CalculationRequest
{
    public required decimal Left { get; init; }
    public required decimal Right { get; init; }
    public required TaskCompletionSource<decimal> Completion { get; init; }
}

public sealed class ComCalculationHostedService : BackgroundService
{
    private readonly Channel<CalculationRequest> _requests = Channel.CreateUnbounded<CalculationRequest>();
    private readonly ISingleThreadedApartmentTaskScheduler _scheduler;
    private readonly string _comDllPath;
    private readonly string _manifestPath;
    private ComObjectHandle<LegacyCalculator.CalculatorClass>? _calculatorHandle;
    private LegacyCalculator.CalculatorClass? _calculator;

    public ComCalculationHostedService(ISingleThreadedApartmentTaskScheduler scheduler)
    {
        _scheduler = scheduler;
        var basePath = AppContext.BaseDirectory;
        _comDllPath = Path.Combine(basePath, "LegacyCalculator.dll");
        _manifestPath = Path.Combine(basePath, "LegacyCalculator.manifest");
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        await _scheduler.RunAsync(
            () =>
            {
                var creation = Executor.Create(
                    _comDllPath,
                    _manifestPath,
                    () => new LegacyCalculator.CalculatorClass());

                if (!creation.Success)
                {
                    throw new InvalidOperationException(
                        "Failed to initialize the COM calculator.",
                        creation.Exception);
                }

                _calculatorHandle = creation.Value
                    ?? throw new InvalidOperationException("The COM calculator handle was not created.");
                _calculator = _calculatorHandle.ComObject
                    ?? throw new InvalidOperationException("The COM calculator instance was not created.");

                return 0;
            },
            cancellationToken);

        await base.StartAsync(cancellationToken);
    }

    public async Task<decimal> AddAsync(decimal left, decimal right, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<decimal>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var registration = cancellationToken.Register(
            static state => ((TaskCompletionSource<decimal>)state!).TrySetCanceled(),
            completion);

        await _requests.Writer.WriteAsync(
            new CalculationRequest
            {
                Left = left,
                Right = right,
                Completion = completion,
            },
            cancellationToken);

        return await completion.Task;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var request in _requests.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                var value = await _scheduler.RunAsync(
                    () =>
                    {
                        if (_calculator is null)
                        {
                            throw new InvalidOperationException("The COM calculator is not initialized.");
                        }

                        return _calculator.Add(request.Left, request.Right);
                    },
                    stoppingToken);

                request.Completion.TrySetResult(value);
            }
            catch (Exception ex)
            {
                request.Completion.TrySetException(ex);
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _requests.Writer.TryComplete();
        await base.StopAsync(cancellationToken);

        await _scheduler.RunAsync(
            () =>
            {
                if (_calculatorHandle is not null)
                {
                    var release = Executor.Free(_calculatorHandle);

                    _calculator = null;
                    _calculatorHandle = null;

                    if (!release.Success)
                    {
                        throw new InvalidOperationException(
                            "Failed to release the COM calculator.",
                            release.Exception);
                    }
                }

                return 0;
            },
            cancellationToken);
    }
}
```

Register it once and expose the same instance both as hosted service and as an injectable service. Register the scheduler as a singleton so it owns a single STA thread for the app lifetime:

```csharp
builder.Services.AddSingleton<ISingleThreadedApartmentTaskScheduler>(
    _ => new SingleThreadedApartmentTaskScheduler(
        new SingleThreadedApartmentTaskSchedulerOptions { ThreadName = "App-STA" }));
builder.Services.AddSingleton<ComCalculationHostedService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ComCalculationHostedService>());
```

This pattern is useful when:

- the COM object must always be created and used on the same STA thread;
- parallel calls would deadlock or hang the COM server;
- you want the rest of the application to remain async while the COM work is serialized behind the queue.

### Multiple different COM components in the same app

Each `SingleThreadedApartmentTaskScheduler` instance owns one reusable STA thread. If you send every COM component through the same instance, all calls will be serialized on that STA thread. If you need parallelism across components, create one scheduler instance per STA lane.

When the components are unrelated and do not need to share the same long-lived STA-bound instance, prefer `SingleThreadedApartmentTask.RunAsync(...)` or `SingleThreadedApartmentTask.RunWithTimeoutAsync(...)`. Each call gets its own temporary STA thread, so different COM components can run independently.

```csharp
using System.Runtime.InteropServices;
using AdaskoTheBeAsT.Interop.COM;
using AdaskoTheBeAsT.Interop.Threading;

public sealed class MultiComFacade
{
    private readonly string _calculatorDllPath;
    private readonly string _calculatorManifestPath;
    private readonly string _reportDllPath;
    private readonly string _reportManifestPath;

    public MultiComFacade(
        string calculatorDllPath,
        string calculatorManifestPath,
        string reportDllPath,
        string reportManifestPath)
    {
        _calculatorDllPath = calculatorDllPath;
        _calculatorManifestPath = calculatorManifestPath;
        _reportDllPath = reportDllPath;
        _reportManifestPath = reportManifestPath;
    }

    public Task<decimal> AddAsync(decimal left, decimal right, CancellationToken cancellationToken)
        => SingleThreadedApartmentTask.RunWithTimeoutAsync(
            TimeSpan.FromSeconds(10),
            () =>
            {
                decimal result = default;
                LegacyCalculator.CalculatorClass? calculator = null;

                var execution = Executor.Execute(_calculatorDllPath, _calculatorManifestPath, () =>
                {
                    calculator = new LegacyCalculator.CalculatorClass();
                    result = calculator.Add(left, right);
                });

                if (calculator is not null)
                {
                    Marshal.FinalReleaseComObject(calculator);
                }

                if (!execution.Success)
                {
                    throw new InvalidOperationException(
                        "The calculator COM call failed.",
                        execution.Exception);
                }

                return result;
            },
            cancellationToken);

    public Task<string> BuildReportAsync(int reportId, CancellationToken cancellationToken)
        => SingleThreadedApartmentTask.RunWithTimeoutAsync(
            TimeSpan.FromSeconds(30),
            () =>
            {
                string report = string.Empty;
                LegacyReporting.ReportGeneratorClass? generator = null;

                var execution = Executor.Execute(_reportDllPath, _reportManifestPath, () =>
                {
                    generator = new LegacyReporting.ReportGeneratorClass();
                    report = generator.Build(reportId);
                });

                if (generator is not null)
                {
                    Marshal.FinalReleaseComObject(generator);
                }

                if (!execution.Success)
                {
                    throw new InvalidOperationException(
                        "The report COM call failed.",
                        execution.Exception);
                }

                return report;
            },
            cancellationToken);
}
```

That lets you do this:

```csharp
var addTask = multiComFacade.AddAsync(10m, 5m, cancellationToken);
var reportTask = multiComFacade.BuildReportAsync(42, cancellationToken);

await Task.WhenAll(addTask, reportTask);
```

#### If the COM object should be instantiated only once

Then keep using the `ComCalculationHostedService` / `SingleThreadedApartmentTaskScheduler` pattern for that component. `SingleThreadedApartmentTask` creates a new temporary STA thread per call, so it is the right choice only when the COM object is created, used, and released inside that single invocation.

For a reusable COM instance:

1. create it once in `StartAsync` on the injected scheduler instance with `Executor.Create(...)`;
2. keep the returned `ComObjectHandle<T>` alive for the whole hosted-service lifetime;
3. store the COM instance in a field;
4. route every operation back through the same `ISingleThreadedApartmentTaskScheduler` instance via `_scheduler.RunAsync(...)`;
5. release the handle in `StopAsync` with `Executor.Free(...)` on the same scheduler thread.

The call path for each request then looks like this:

```csharp
public Task<decimal> AddAsync(decimal left, decimal right, CancellationToken cancellationToken)
    => _scheduler.RunAsync(
        () =>
        {
            if (_calculator is null)
            {
                throw new InvalidOperationException("The COM calculator is not initialized.");
            }

            return _calculator.Add(left, right);
        },
        cancellationToken);
```

#### If several different COM objects should each be instantiated only once

If those COM objects can all live on the same STA thread, create them together in one hosted service with `Executor.Create(...)`, keep one `ComObjectHandle<T>` per object, and reuse the instances through a single injected `ISingleThreadedApartmentTaskScheduler` instance.

```csharp
private ComObjectHandle<LegacyCalculator.CalculatorClass>? _calculatorHandle;
private LegacyCalculator.CalculatorClass? _calculator;
private ComObjectHandle<LegacyReporting.ReportGeneratorClass>? _reportGeneratorHandle;
private LegacyReporting.ReportGeneratorClass? _reportGenerator;

public override async Task StartAsync(CancellationToken cancellationToken)
{
    await _scheduler.RunAsync(
        () =>
        {
            var calculatorCreation = Executor.Create(
                _calculatorDllPath,
                _calculatorManifestPath,
                () => new LegacyCalculator.CalculatorClass());

            if (!calculatorCreation.Success)
            {
                throw new InvalidOperationException(
                    "Failed to initialize the calculator COM component.",
                    calculatorCreation.Exception);
            }

            _calculatorHandle = calculatorCreation.Value
                ?? throw new InvalidOperationException("The calculator COM handle was not created.");
            _calculator = _calculatorHandle.ComObject
                ?? throw new InvalidOperationException("The calculator COM instance was not created.");

            var reportCreation = Executor.Create(
                _reportDllPath,
                _reportManifestPath,
                () => new LegacyReporting.ReportGeneratorClass());

            if (!reportCreation.Success)
            {
                throw new InvalidOperationException(
                    "Failed to initialize the reporting COM component.",
                    reportCreation.Exception);
            }

            _reportGeneratorHandle = reportCreation.Value
                ?? throw new InvalidOperationException("The reporting COM handle was not created.");
            _reportGenerator = _reportGeneratorHandle.ComObject
                ?? throw new InvalidOperationException("The reporting COM instance was not created.");

            return 0;
        },
        cancellationToken);

    await base.StartAsync(cancellationToken);
}

public Task<decimal> AddAsync(decimal left, decimal right, CancellationToken cancellationToken)
    => _scheduler.RunAsync(
        () =>
        {
            if (_calculator is null)
            {
                throw new InvalidOperationException("The calculator COM component is not initialized.");
            }

            return _calculator.Add(left, right);
        },
        cancellationToken);

public Task<string> BuildReportAsync(int reportId, CancellationToken cancellationToken)
    => _scheduler.RunAsync(
        () =>
        {
            if (_reportGenerator is null)
            {
                throw new InvalidOperationException("The reporting COM component is not initialized.");
            }

            return _reportGenerator.Build(reportId);
        },
        cancellationToken);
```

Release every handle in `StopAsync` on the same scheduler thread by calling `Executor.Free(...)`, just like in the single-component hosted service example above.

This gives you one app-wide STA lane where multiple COM components are instantiated once and reused safely.

If those reusable components must run in parallel, a single `SingleThreadedApartmentTaskScheduler` instance is not enough because it is one STA thread. In that case create multiple scheduler instances — one per STA lane — and route each COM component through its own instance (register them keyed in DI, or wrap them in per-component services).

Use this mixed approach in one application:

- use one `SingleThreadedApartmentTaskScheduler` instance for each component that must stay alive on a dedicated STA thread across many requests;
- use `SingleThreadedApartmentTask` for one-off or isolated calls to other COM components;
- keep all operations for a single STA-bound COM instance inside the same scheduled workflow.

### Periodic Task with Cancellation
```csharp
public async Task RunPeriodicTaskAsync(
    ISingleThreadedApartmentTaskScheduler scheduler,
    CancellationToken ct)
{
    await scheduler.RunAsync((StaYield staYield) => {
        while (!ct.IsCancellationRequested) {
            PerformWork();

            // Sleep 10s while keeping message pump active
            staYield.Sleep(10000);
        }
    }, ct);
}
```

## 🏗️ Technical Details

- **Frameworks**: .NET 10.0-windows, .NET 9.0-windows, .NET 8.0-windows, .NET Framework 4.8.1, 4.8, 4.7.2, 4.7.1, 4.7, 4.6.2
- **Platform**: Windows only (uses Win32 APIs for message pumps and COM)
- **P/Invoke**: Uses modern `LibraryImport` source generators on .NET 8+ and `DllImport` on .NET Framework targets
- **Thread Safety**: All APIs are thread-safe
- **Async/Await**: Full async/await support with proper ConfigureAwait usage

## 🧭 Architecture Decision Records

- [ADR 0001: Harden timeout cancellation and STA scheduler dispatch](docs/adr/0001-hardening-timeouts-and-sta-scheduler.md)

Recent hardening changes were made so that:

- `TimeoutAfterAsync` correctly distinguishes caller cancellation from an actual timeout;
- `SingleThreadedApartmentTaskScheduler` preserves original exceptions from queued work;
- queued STA operations no longer risk returning a canceled wrapper task after the work has already been accepted for execution.

## 📝 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## 🤝 Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

## ⚠️ Known Considerations

- Windows-only library (relies on Win32 message pump and OLE APIs)
- Each `SingleThreadedApartmentTaskScheduler` instance creates a persistent background STA thread; dispose the instance to stop it deterministically
- STA threads have lower performance than MTA threads - use only when necessary (COM interop, clipboard, etc.)

## 🔄 Migration Guide

### From 2.x to 3.0

`SingleThreadedApartmentTaskScheduler` has been converted from a **static class** into an **instance class** that implements `ISingleThreadedApartmentTaskScheduler` and `IDisposable`. Each instance owns its own STA thread, which fixes several long-standing limitations (no deterministic shutdown, no isolation between tests, no way to run parallel STA lanes, no DI story).

This is a **breaking change**. The static `SingleThreadedApartmentTaskScheduler.RunAsync(...)` / `Shutdown()` members no longer exist; callers must now create an instance.

#### Before (2.x)

```csharp
var task1 = SingleThreadedApartmentTaskScheduler.RunAsync(() => ComOperation1(), ct);
var task2 = SingleThreadedApartmentTaskScheduler.RunAsync(() => ComOperation2(), ct);
await Task.WhenAll(task1, task2);

SingleThreadedApartmentTaskScheduler.Shutdown();
```

#### After (3.0)

```csharp
using var scheduler = new SingleThreadedApartmentTaskScheduler();

var task1 = scheduler.RunAsync(() => ComOperation1(), ct);
var task2 = scheduler.RunAsync(() => ComOperation2(), ct);
await Task.WhenAll(task1, task2);

// Disposing the instance shuts the STA thread down deterministically
// and cancels any queued-but-not-yet-executed items.
```

#### Recommended pattern: register once as a singleton

For application-wide use, register one scheduler per STA lane as a DI singleton. Use `SingleThreadedApartmentTaskSchedulerOptions` to configure it — the constructor takes an options object so there is no ambiguous `string` parameter for the container to resolve:

```csharp
builder.Services.AddSingleton<ISingleThreadedApartmentTaskScheduler>(
    _ => new SingleThreadedApartmentTaskScheduler(
        new SingleThreadedApartmentTaskSchedulerOptions { ThreadName = "App-STA" }));
```

Then inject `ISingleThreadedApartmentTaskScheduler` wherever you previously called the static API. The DI container will dispose the scheduler on application shutdown.

#### Quick fix via a shared static (not recommended, but source-minimal)

If you want to postpone the full migration, wrap one instance behind your own static helper:

```csharp
internal static class AppSta
{
    public static ISingleThreadedApartmentTaskScheduler Default { get; }
        = new SingleThreadedApartmentTaskScheduler(
            new SingleThreadedApartmentTaskSchedulerOptions { ThreadName = "App-STA" });
}

// Call sites:
await AppSta.Default.RunAsync(() => ComOperation(), ct);
```

This restores the old call-site ergonomics but also retains the old drawback of a single process-wide STA thread that never shuts down before process exit.

## 📋 Changelog

### 3.0.0 (breaking)

- **Breaking:** `SingleThreadedApartmentTaskScheduler` is now an instance class (previously `static`). See the Migration Guide above.
- Added `ISingleThreadedApartmentTaskScheduler` interface to enable dependency injection and mocking.
- Added `IDisposable` support — disposing the scheduler joins its STA thread, cancels any pending queued items, and releases the underlying synchronization handles.
- Added `ObjectDisposedException` thrown from `RunAsync` after disposal.
- Added `SingleThreadedApartmentTaskSchedulerOptions` configuration class (currently exposes `ThreadName`). The constructor takes an options instance, which plays nicely with DI and `Microsoft.Extensions.Options`.
- Surface OLE initialization failure to callers instead of silently leaving the thread dead: subsequent `RunAsync` calls fault with an `InvalidOperationException` containing the HRESULT.
- Multiple scheduler instances can now coexist, each with its own STA thread (enables per-component STA lanes and isolated unit tests).

### 2.x

- `MutexHelper`, `SingleThreadedApartmentTask`, `TaskExtension`, and `StaYield` hardened against cancellation-vs-timeout races and exception wrapping (see ADR 0001).