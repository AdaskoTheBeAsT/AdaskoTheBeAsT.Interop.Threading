# AdaskoTheBeAsT.Interop.Threading

[![NuGet](https://img.shields.io/nuget/v/AdaskoTheBeAsT.Interop.Threading.svg)](https://www.nuget.org/packages/AdaskoTheBeAsT.Interop.Threading/)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

A high-performance, production-ready C# library for advanced threading scenarios on Windows. Provides robust utilities for cross-process mutex synchronization, STA (Single-Threaded Apartment) task execution, and task timeout management with proper message pump handling.

## 🚀 Why Use This Library?

- **Cross-Process Synchronization**: Global mutexes with proper security settings and abandoned mutex recovery
- **STA Thread Support**: Execute code in STA context with full COM interop support and message pump handling
- **Production-Ready**: Handles edge cases like abandoned mutexes, TickCount wraparound, and cancellation token propagation
- **Multi-Framework Support**: Targets .NET Standard 2.0, .NET 8.0, .NET 9.0, and .NET 10.0 with optimized P/Invoke for each
- **Zero External Dependencies**: Only uses System.Threading.AccessControl for enhanced security

## 📦 Installation

```bash
dotnet add package AdaskoTheBeAsT.Interop.Threading
```

Or via NuGet Package Manager:
```powershell
Install-Package AdaskoTheBeAsT.Interop.Threading
```

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
    (StaYield yield) => {
        for (int i = 0; i < 1000; i++) {
            DoWork(i);
            // Pump messages periodically to keep UI responsive
            yield.Occasionally();
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

**Usage:**
```csharp
// Multiple tasks can share the same STA thread
var task1 = SingleThreadedApartmentTaskScheduler.RunAsync(() => ComOperation1());
var task2 = SingleThreadedApartmentTaskScheduler.RunAsync(() => ComOperation2());

await Task.WhenAll(task1, task2);
```

**With StaYield:**
```csharp
await SingleThreadedApartmentTaskScheduler.RunAsync((StaYield yield) => {
    while (!condition) {
        // Wait for condition while pumping messages
        yield.SpinUntil(() => CheckCondition(), checkEveryMs: 10);
    }
    
    // Or sleep without blocking the message pump
    yield.Sleep(1000);
});
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
var result = await SingleThreadedApartmentTask.RunAsync((StaYield yield) => {
    var items = GetLargeItemList();
    
    foreach (var item in items) {
        ProcessItem(item);
        
        // Pump messages every 15ms (default) during long loops
        yield.Occasionally();
    }
    
    // Wait for a condition without blocking messages
    yield.SpinUntil(() => IsReady(), checkEveryMs: 10);
    
    // Sleep while keeping message pump active
    yield.Sleep(1000);
    
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

For repeated COM calculations that must stay serialized on one STA thread, use `SingleThreadedApartmentTaskScheduler.RunAsync(...)`.

See [Using AdaskoTheBeAsT.Interop.Threading with AdaskoTheBeAsT.Interop.COM](docs/using-with-adaskothebeast-interop-com.md) for a full guide.

### Hosted Service for serialized COM requests
If the COM server hangs when multiple callers invoke it in parallel, put a queue in front of a single STA worker.

The pattern is:

1. create the COM object once in `StartAsync` while the manifest activation context is active;
2. accept incoming requests through a queue;
3. process each request through `SingleThreadedApartmentTaskScheduler.RunAsync(...)` so every call stays on the same STA thread and runs one-by-one.

```csharp
using System.Runtime.InteropServices;
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
    private readonly string _comDllPath;
    private readonly string _manifestPath;
    private LegacyCalculator.CalculatorClass? _calculator;

    public ComCalculationHostedService()
    {
        var basePath = AppContext.BaseDirectory;
        _comDllPath = Path.Combine(basePath, "LegacyCalculator.dll");
        _manifestPath = Path.Combine(basePath, "LegacyCalculator.manifest");
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        await SingleThreadedApartmentTaskScheduler.RunAsync(
            () =>
            {
                var execution = Executor.Execute(_comDllPath, _manifestPath, () =>
                {
                    _calculator = new LegacyCalculator.CalculatorClass();
                });

                if (!execution.Success)
                {
                    throw new InvalidOperationException(
                        "Failed to initialize the COM calculator.",
                        execution.Exception);
                }

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
                var value = await SingleThreadedApartmentTaskScheduler.RunAsync(
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

        await SingleThreadedApartmentTaskScheduler.RunAsync(
            () =>
            {
                if (_calculator is not null)
                {
                    Marshal.FinalReleaseComObject(_calculator);
                    _calculator = null;
                }

                return 0;
            },
            cancellationToken);

        await base.StopAsync(cancellationToken);
    }
}
```

Register it once and expose the same instance both as hosted service and as an injectable service:

```csharp
builder.Services.AddSingleton<ComCalculationHostedService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ComCalculationHostedService>());
```

This pattern is useful when:

- the COM object must always be created and used on the same STA thread;
- parallel calls would deadlock or hang the COM server;
- you want the rest of the application to remain async while the COM work is serialized behind the queue.

### Multiple different COM components in the same app

`SingleThreadedApartmentTaskScheduler` is one reusable STA thread for the whole process. If you send every COM component through it, all calls will be serialized on that same STA thread.

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

1. create it once in `StartAsync` on the scheduler thread;
2. store it in a field;
3. route every operation back through `SingleThreadedApartmentTaskScheduler.RunAsync(...)`;
4. release it in `StopAsync`.

The call path then looks like this:

```csharp
public Task<decimal> AddAsync(decimal left, decimal right, CancellationToken cancellationToken)
    => SingleThreadedApartmentTaskScheduler.RunAsync(
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

If those COM objects can all live on the same STA thread, create them together in one hosted service and reuse them through `SingleThreadedApartmentTaskScheduler`.

```csharp
private LegacyCalculator.CalculatorClass? _calculator;
private LegacyReporting.ReportGeneratorClass? _reportGenerator;

public override async Task StartAsync(CancellationToken cancellationToken)
{
    await SingleThreadedApartmentTaskScheduler.RunAsync(
        () =>
        {
            var calculatorExecution = Executor.Execute(_calculatorDllPath, _calculatorManifestPath, () =>
            {
                _calculator = new LegacyCalculator.CalculatorClass();
            });

            if (!calculatorExecution.Success)
            {
                throw new InvalidOperationException(
                    "Failed to initialize the calculator COM component.",
                    calculatorExecution.Exception);
            }

            var reportExecution = Executor.Execute(_reportDllPath, _reportManifestPath, () =>
            {
                _reportGenerator = new LegacyReporting.ReportGeneratorClass();
            });

            if (!reportExecution.Success)
            {
                throw new InvalidOperationException(
                    "Failed to initialize the reporting COM component.",
                    reportExecution.Exception);
            }

            return 0;
        },
        cancellationToken);

    await base.StartAsync(cancellationToken);
}

public Task<decimal> AddAsync(decimal left, decimal right, CancellationToken cancellationToken)
    => SingleThreadedApartmentTaskScheduler.RunAsync(
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
    => SingleThreadedApartmentTaskScheduler.RunAsync(
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

Release every COM object in `StopAsync` on the same scheduler thread, just like in the single-component hosted service example above.

This gives you one app-wide STA lane where multiple COM components are instantiated once and reused safely.

If those reusable components must run in parallel, `SingleThreadedApartmentTaskScheduler` alone is not enough because it is a single global STA thread. In that case you need separate dedicated STA workers.

Use this mixed approach in one application:

- use `SingleThreadedApartmentTaskScheduler` for a component that must stay alive on one dedicated STA thread across many requests;
- use `SingleThreadedApartmentTask` for one-off or isolated calls to other COM components;
- keep all operations for a single STA-bound COM instance inside the same scheduled workflow.

### Periodic Task with Cancellation
```csharp
public async Task RunPeriodicTaskAsync(CancellationToken ct) {
    await SingleThreadedApartmentTaskScheduler.RunAsync((StaYield yield) => {
        while (!ct.IsCancellationRequested) {
            PerformWork();
            
            // Sleep 10s while keeping message pump active
            yield.Sleep(10000);
        }
    }, ct);
}
```

## 🏗️ Technical Details

- **Frameworks**: .NET Standard 2.0, .NET 8.0-windows, .NET 9.0-windows, .NET 10.0-windows
- **Platform**: Windows only (uses Win32 APIs for message pumps and COM)
- **P/Invoke**: Uses modern `LibraryImport` source generators on .NET 8+ for better performance
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
- The `SingleThreadedApartmentTaskScheduler` creates a persistent background thread on first use
- STA threads have lower performance than MTA threads - use only when necessary (COM interop, clipboard, etc.)