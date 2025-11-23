# AdaskoTheBeAsT.Interop.Threading

[![NuGet](https://img.shields.io/nuget/v/AdaskoTheBeAsT.Interop.Threading.svg)](https://www.nuget.org/packages/AdaskoTheBeAsT.Interop.Threading/)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

A high-performance, production-ready C# library for advanced threading scenarios on Windows. Provides robust utilities for cross-process mutex synchronization, STA (Single-Threaded Apartment) task execution, and task timeout management with proper message pump handling.

## 🚀 Why Use This Library?

- **Cross-Process Synchronization**: Global mutexes with proper security settings and abandoned mutex recovery
- **STA Thread Support**: Execute code in STA context with full COM interop support and message pump handling
- **Production-Ready**: Handles edge cases like abandoned mutexes, TickCount wraparound, and cancellation token propagation
- **Multi-Framework Support**: Targets .NET Standard 2.0, .NET 8.0, and .NET 9.0 with optimized P/Invoke for each
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

- **Frameworks**: .NET Standard 2.0, .NET 8.0-windows, .NET 9.0-windows
- **Platform**: Windows only (uses Win32 APIs for message pumps and COM)
- **P/Invoke**: Uses modern `LibraryImport` source generators on .NET 8+ for better performance
- **Thread Safety**: All APIs are thread-safe
- **Async/Await**: Full async/await support with proper ConfigureAwait usage

## 📝 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## 🤝 Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

## ⚠️ Known Considerations

- Windows-only library (relies on Win32 message pump and OLE APIs)
- The `SingleThreadedApartmentTaskScheduler` creates a persistent background thread on first use
- STA threads have lower performance than MTA threads - use only when necessary (COM interop, clipboard, etc.)