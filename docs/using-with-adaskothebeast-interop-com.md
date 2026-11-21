# Using `AdaskoTheBeAsT.Interop.Threading` with `AdaskoTheBeAsT.Interop.COM` and `AdaskoTheBeAsT.Interop.Execution`

`AdaskoTheBeAsT.Interop.COM`, `AdaskoTheBeAsT.Interop.Threading`, and `AdaskoTheBeAsT.Interop.Execution` solve complementary parts of hosting native and COM workloads from modern .NET:

- `AdaskoTheBeAsT.Interop.COM` handles registration-free COM activation (side-by-side manifests, activation contexts, class-factory scopes, and Windows Nano Server STA shim preparation).
- `AdaskoTheBeAsT.Interop.Threading` provides a dedicated single-STA thread (`SingleThreadedApartmentTaskScheduler`), synchronous and asynchronous one-off STA execution (`SingleThreadedApartmentTask.Run` / `RunAsync`), standalone STA apartment leases (`StaApartment.EnterScope`), portable thread-ownership guards (`ThreadAffinity`), managed culture and Win32 thread locale hygiene (`ThreadCultureScope` and scheduler options), cooperative `StaYield` message pumping, `Task` / `ValueTask` timeouts, and cross-process named mutexes, with **zero package dependency** on `Interop.COM` or `Interop.Execution`.
- `AdaskoTheBeAsT.Interop.Execution` provides multi-worker pools (`ExecutionWorkerPool<TSession>`) with typed stateful sessions, queue routing, automatic session recycling, and ASP.NET Core hosted-service integration.

## Choosing between `SingleThreadedApartmentTaskScheduler` and `ExecutionWorkerPool<TSession>`

Pick the orchestration model that matches your concurrency and state requirements:

| Capability / Requirement | `SingleThreadedApartmentTaskScheduler` (`Interop.Threading`) | `ExecutionWorkerPool<TSession>` (`Interop.Execution`) |
| --- | --- | --- |
| Thread topology | **One** dedicated STA thread per scheduler instance (`MaxStackSize` configurable) | **Pool of `N`** dedicated STA (or MTA) worker threads |
| State model | Ad-hoc heterogeneous delegates (`Func<T>`, `Action`, `Action<StaYield>`) plus optional `CreateThreadScope` | Strongly typed per-worker `TSession` initialized once per worker lifecycle |
| Cooperative message pumping inside long loops | Built-in (`RunCooperativeAsync` + `StaYield.Occasionally` / `Sleep` / `SpinUntil`) | Delegate-controlled; best suited to discrete request/response operations |
| Load balancing & horizontal throughput | Single serialized queue (`MaximumPendingWorkItems` admission cap) | Multi-worker routing (such as `LeastQueued`) across isolated sessions |
| Automatic session recycling | Manual / owner-managed scheduler lifecycle | Built-in (`MaxOperationsPerSession`, lifetime budgets, faulted-session replacement) |
| Windows Nano Server, locale & legacy 32-bit COM hygiene | `BeforeThreadStart`, `VerifyNativeStaApartment`, `ThreadCulture`, `WindowsThreadLocaleId`, `ResetThreadCultureAndLocaleAfterWorkItem`, `PostWorkItemMessagePumpBatchSize`, `FreeUnusedComLibrariesOnShutdown`, `ResetFloatingPointMaskAfterWorkItem`, `MaxStackSize` | Compose `ThreadAffinity`, `StaApartment.EnterScope()`, and `ThreadCultureScope` inside `TSession` |
| Cross-process serialization | Compose `MutexHelper.RunInMutex` with synchronous `SingleThreadedApartmentTask.Run` | Compose with `MutexHelper.RunInMutex` inside synchronous session operations |

**Rule of thumb:**
- Choose **`SingleThreadedApartmentTaskScheduler`** when callers must share **one** STA apartment, interleave heterogeneous methods on the same COM graph, pump Win32/OLE messages cooperatively via `StaYield`, or react to `WM_QUIT`.
- Choose **`ExecutionWorkerPool<TSession>`** when each request can run against an independent STA COM instance and you need **`N` parallel STA workers**, session recycling after `K` operations, or turnkey `IHostedService` pool scaling.

## Timeout and cancellation stop waiting, not synchronous native execution

> **Critical contract:** A per-call timeout (`RunAsync(..., timeout, token)`, `RunCooperativeAsync`, `RunWithTimeoutAsync`, or `TimeoutAfterAsync`) and caller `CancellationToken` **only stop the caller's wait** (or signal cooperative checkpoints *between* native calls).
>
> Neither `Interop.Threading` nor `Interop.Execution` can safely preempt, abort, or unwind an already-running synchronous native/COM method call on its STA thread:
>
> - If a caller times out or cancels while a synchronous COM call is blocked inside native code, the caller receives `TimeoutException` or `OperationCanceledException` promptly, **but the native call continues running on the STA worker** and retains its COM objects / session state until it returns.
> - Subsequent queued work items (and STA scope disposal / `OleUninitialize` during shutdown) remain serialized behind that active native call.
> - `ShutdownAsync(timeout, token)` bounds only how long the caller waits for worker exit; a later `Dispose()` still joins the OS thread so per-thread COM state is never torn down beneath executing native code.
> - When you require hard forcible termination of hung native libraries, isolate the workload in a **separate child process** and terminate the process on deadline expiration.

## Dependency-free startup wiring for Windows Nano Server (`BeforeThreadStart`)

On Windows Nano Server without `win32u.dll`, CoreCLR invokes `combase!CoInitializeEx(COINIT_APARTMENTTHREADED)` inside `Thread.Start()` **before** the managed thread entry method begins executing. If an in-process STA shim (such as `NanoServerOptions.RefreshStaState` in `AdaskoTheBeAsT.Interop.COM`) has not been initialized yet, CoreCLR's implicit STA initialization can silently fall back to MTA or leave COM uninitialized even though `Thread.GetApartmentState()` still reports `ApartmentState.STA`.

Wire `BeforeThreadStart` via a standard `Action` delegate; no direct reference from `Interop.Threading` to `Interop.COM` is needed:

```csharp
using AdaskoTheBeAsT.Interop.COM;
using AdaskoTheBeAsT.Interop.Threading;

using var scheduler = new SingleThreadedApartmentTaskScheduler(
    new SingleThreadedApartmentTaskSchedulerOptions
    {
        ThreadName = "Nano-COM-STA",
        BeforeThreadStart = NanoServerOptions.RefreshStaState,
        VerifyNativeStaApartment = true, // default: verifies CoGetApartmentType on the worker thread
    });
```

What happens during construction:
1. All option properties are snapshotted before callbacks run.
2. `BeforeThreadStart` executes synchronously on the **constructing thread** before `Thread.Start()`.
3. On the worker thread, after `OleInitialize`, `VerifyNativeStaApartment` (enabled by default) queries `combase!CoGetApartmentType`. If COM reports `CO_E_NOTINITIALIZED`, the scheduler calls `CoInitializeEx(COINIT_APARTMENTTHREADED)` and balances `CoUninitialize()` on exit; if the native apartment is MTA instead of STA, the constructor throws `InvalidOperationException` immediately rather than letting STA-only coclasses fail unpredictably later.

## Long-lived per-thread COM activation scopes (`CreateThreadScope`)

Activating and deactivating a side-by-side manifest or class-factory registration on *every* `RunAsync` call adds avoidable `CreateActCtxW` / `CoRegisterClassObject` overhead and prevents keeping thread-affinitized COM objects alive across calls.

In **5.0**, `CreateThreadScope` (`Func<IDisposable?>`) runs **once** on the owned STA thread during startup (after OLE/STA verification and before work is accepted) and disposes the returned `IDisposable` **once** on that exact STA thread during shutdown, before `CoFreeUnusedLibrariesEx` and `CoUninitialize` / `OleUninitialize`:

```csharp
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using AdaskoTheBeAsT.Interop.COM;
using AdaskoTheBeAsT.Interop.Threading;

public sealed class ScopedComCalculatorService : IDisposable
{
    private readonly SingleThreadedApartmentTaskScheduler _scheduler;
    private LegacyCalculator.CalculatorClass? _calculator;

    public ScopedComCalculatorService(string comDllPath, string manifestPath)
    {
        _scheduler = new SingleThreadedApartmentTaskScheduler(
            new SingleThreadedApartmentTaskSchedulerOptions
            {
                ThreadName = "Calc-STA",
                BeforeThreadStart = NanoServerOptions.RefreshStaState,
                CreateThreadScope = () =>
                {
                    // Runs on the owned STA thread before any RunAsync delegate executes.
                    var activationScope = RegistrationFreeComScope.Enter(comDllPath, manifestPath);
                    try
                    {
                        _calculator = new LegacyCalculator.CalculatorClass();
                        return new StaSessionLifetime(
                            onDispose: () =>
                            {
                                if (_calculator is not null && Marshal.IsComObject(_calculator))
                                {
                                    Marshal.FinalReleaseComObject(_calculator);
                                }

                                _calculator = null;
                                activationScope.Dispose();
                            });
                    }
                    catch
                    {
                        activationScope.Dispose();
                        throw;
                    }
                },
                // Optional hygiene for legacy VB6 / Delphi / C++ STA COM servers:
                PostWorkItemMessagePumpBatchSize = 64,
                FreeUnusedComLibrariesOnShutdown = true,
                ResetFloatingPointMaskAfterWorkItem = true, // active on Windows x86 only
            });
    }

    public Task<decimal> AddAsync(decimal left, decimal right, CancellationToken cancellationToken)
        => _scheduler.RunAsync(
            () => _calculator!.Add(left, right),
            cancellationToken);

    public void Dispose() => _scheduler.Dispose();

    private sealed class StaSessionLifetime(Action onDispose) : IDisposable
    {
        public void Dispose() => onDispose();
    }
}
```

Replace `RegistrationFreeComScope.Enter(...)` with the scope factory from your `AdaskoTheBeAsT.Interop.COM` setup (or your own `IDisposable` wrapper around `ActCtx` / class-factory registration). Because `CreateThreadScope` uses `System.IDisposable`, `Interop.Threading` remains dependency-free while guaranteeing:

1. If `CreateThreadScope` throws, scheduler construction faults with that original exception and tears down owned COM/OLE initialization cleanly.
2. During shutdown, queued pending work items are canceled first, then `scope.Dispose()` executes on the owned STA thread, followed (when opted in) by `CoFreeUnusedLibrariesEx(0, 0)`, one bounded message-pump drain (`preserveQuit: true`), and balanced `CoUninitialize` / `OleUninitialize`.
3. On 32-bit `win-x86`, enabling `ResetFloatingPointMaskAfterWorkItem = true` clears pending x87/SSE status flags (`_clearfp`) and restores all six exception masks (`_MCW_EM` via `ucrtbase!__control87_2`) after `CreateThreadScope` and after every work item, preserving callers' rounding and precision modes while preventing Delphi/VB6/C++ COM servers that unmask `#IE`/`#ZE`/`#OE` from faulting subsequent managed floating-point code (or triggering CoreCLR `win-x86` `FailFast`).

## One-off COM calculation with timeout

Use `SingleThreadedApartmentTask.RunWithTimeoutAsync` when the call is isolated and you do not need to reuse a dedicated STA scheduler:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using AdaskoTheBeAsT.Interop.COM;
using AdaskoTheBeAsT.Interop.Threading;

public sealed class ComCalculator
{
    private readonly string _comDllPath;
    private readonly string _manifestPath;

    public ComCalculator(string comDllPath, string manifestPath)
    {
        _comDllPath = comDllPath;
        _manifestPath = manifestPath;
    }

    public Task<decimal> AddAsync(decimal left, decimal right, CancellationToken cancellationToken)
        => SingleThreadedApartmentTask.RunWithTimeoutAsync(
            TimeSpan.FromSeconds(10),
            () =>
            {
                decimal result = default;

                var execution = Executor.Execute(_comDllPath, _manifestPath, () =>
                {
                    var calculator = new LegacyCalculator.CalculatorClass();
                    result = calculator.Add(left, right);
                });

                if (!execution.Success)
                {
                    throw new InvalidOperationException(
                        "The COM calculation failed.",
                        execution.Exception);
                }

                return result;
            },
            cancellationToken);
}
```

## Per-call manifest activation on a shared STA scheduler

When callers share an `ISingleThreadedApartmentTaskScheduler` injected via DI and different calls activate different manifests per operation, you can still call `Executor.Execute` inside `RunAsync`:

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AdaskoTheBeAsT.Interop.COM;
using AdaskoTheBeAsT.Interop.Threading;

public sealed class ScheduledComCalculator
{
    private readonly ISingleThreadedApartmentTaskScheduler _scheduler;
    private readonly string _comDllPath;
    private readonly string _manifestPath;

    public ScheduledComCalculator(
        ISingleThreadedApartmentTaskScheduler scheduler,
        string comDllPath,
        string manifestPath)
    {
        _scheduler = scheduler;
        _comDllPath = comDllPath;
        _manifestPath = manifestPath;
    }

    public Task<IReadOnlyList<decimal>> CalculateBatchAsync(
        IReadOnlyList<(decimal Left, decimal Right)> inputs,
        CancellationToken cancellationToken)
        => _scheduler.RunAsync(
            (StaYield yield) =>
            {
                var results = new List<decimal>(inputs.Count);

                var execution = Executor.Execute(_comDllPath, _manifestPath, () =>
                {
                    var calculator = new LegacyCalculator.CalculatorClass();

                    foreach (var input in inputs)
                    {
                        results.Add(calculator.Add(input.Left, input.Right));
                        yield.Occasionally();
                    }
                });

                if (!execution.Success)
                {
                    throw new InvalidOperationException(
                        "The COM batch calculation failed.",
                        execution.Exception);
                }

                return (IReadOnlyList<decimal>)results;
            },
            cancellationToken);
}
```

## Cross-library production recipes

### Recipe 1: Composing `MutexHelper.RunInMutex` with synchronous `SingleThreadedApartmentTask.Run`

OS mutex acquisition (`WaitOne`) and `ReleaseMutex()` must occur synchronously on the **same thread**, so `MutexHelper.RunInMutex` intentionally accepts synchronous `Func<T>` and `Action` delegates rather than `async` callbacks. When a legacy STA COM engine mutates shared global files, registry keys, or non-thread-safe companion DLLs across processes, run `SingleThreadedApartmentTask.Run` inside `MutexHelper.RunInMutex`:

```csharp
using System;
using System.Globalization;
using System.Threading;
using AdaskoTheBeAsT.Interop.COM;
using AdaskoTheBeAsT.Interop.Threading;

public static class CrossProcessStaCalculator
{
    public static decimal CalculateSerialized(
        string comDllPath,
        string manifestPath,
        decimal input,
        CancellationToken cancellationToken)
        => MutexHelper.RunInMutex(
            "MyApp.SharedCalculationState",
            new MutexExecutionOptions
            {
                IsGlobal = false,
                Timeout = TimeSpan.FromSeconds(15),
                FailOnAbandonedMutex = true,
            },
            () => SingleThreadedApartmentTask.Run(
                () =>
                {
                    decimal output = default;
                    var execution = Executor.Execute(comDllPath, manifestPath, () =>
                    {
                        var calculator = new LegacyCalculator.CalculatorClass();
                        output = calculator.Compute(input);
                    });

                    if (!execution.Success)
                    {
                        throw new InvalidOperationException("COM calculation failed.", execution.Exception);
                    }

                    return output;
                },
                new SingleThreadedApartmentTaskOptions
                {
                    ThreadName = "Calc-OneOff-STA",
                    ExecutionMode = StaExecutionMode.DedicatedThread,
                    BeforeThreadStart = NanoServerOptions.RefreshStaState,
                    ThreadCulture = CultureInfo.InvariantCulture,
                    WindowsThreadLocaleId = 0x0409, // en-US decimal dot for OLEAUT32 / VB6
                    ResetFloatingPointMaskBeforeExit = true,
                },
                cancellationToken),
            cancellationToken);
}
```

Why this composition is safe:
- `SingleThreadedApartmentTask.Run` **always joins** any dedicated STA thread it starts before returning—even when `cancellationToken` is canceled mid-call—so `MutexHelper.RunInMutex` never releases the named mutex while native COM code is still mutating protected state.
- Worker exceptions are rethrown via `ExceptionDispatchInfo` with their original exception type and stack trace intact.
- Use `StaExecutionMode.ReuseCurrentStaThread` when the caller may already be an STA thread (for example inside an `[StaFact]` test or outer STA worker) and you want zero thread-creation overhead without draining the caller's `WM_QUIT` message.

### Recipe 2: Using `ThreadAffinity`, `StaApartment.EnterScope()`, and `ThreadCultureScope` inside `ExecutionWorkerPool<TSession>`

When `AdaskoTheBeAsT.Interop.Execution`'s `ExecutionWorkerPool<TSession>` starts worker threads with `UseStaThread = true`, CoreCLR initializes `ApartmentState.STA` via internal `RoInitialize`, which does not register `"OleMainThreadWndClass"` in `combase.dll` until an explicit `CoInitializeEx(COINIT_APARTMENTTHREADED)` or `OleInitialize` call runs on that thread. Combining `ThreadAffinity`, `StaApartment.EnterScope()`, and `ThreadCultureScope` inside `TSession` eliminates manual boilerplate:

```csharp
using System;
using System.Globalization;
using System.Runtime.InteropServices;
using AdaskoTheBeAsT.Interop.Threading;

public sealed class PooledComCalculatorSession : IDisposable
{
    private const int UkEnglishLcid = 0x0809; // en-GB (dot decimal separator)
    private readonly ThreadAffinity _affinity;
    private readonly StaApartmentScope _apartmentScope;
    private readonly IDisposable _activationScope;
    private LegacyCalculator.CalculatorClass? _calculator;
    private bool _disposed;

    public PooledComCalculatorSession(Func<IDisposable> enterManifestScope)
    {
        _affinity = ThreadAffinity.Capture();

        // Verifies STA, increments combase STA refcount (registering OleMainThreadWndClass),
        // and optionally initializes OLE2 when initializeOle: true.
        _apartmentScope = StaApartment.EnterScope(initializeOle: true);
        try
        {
            using (ThreadCultureScope.Enter(
                       CultureInfo.InvariantCulture,
                       CultureInfo.InvariantCulture,
                       UkEnglishLcid))
            {
                _activationScope = enterManifestScope();
                try
                {
                    _calculator = new LegacyCalculator.CalculatorClass();
                }
                catch
                {
                    _activationScope.Dispose();
                    throw;
                }
            }
        }
        catch
        {
            _apartmentScope.Dispose();
            throw;
        }
    }

    public double Compute(double left, double right)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(PooledComCalculatorSession));
        }

        _affinity.ThrowIfWrongThread(nameof(PooledComCalculatorSession));

        using (ThreadCultureScope.Enter(
                   CultureInfo.InvariantCulture,
                   CultureInfo.InvariantCulture,
                   UkEnglishLcid))
        {
            return _calculator!.Calculate(left, right);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _affinity.ThrowIfWrongThread(nameof(PooledComCalculatorSession));
        _disposed = true;

        try
        {
            if (_calculator is not null && Marshal.IsComObject(_calculator))
            {
                Marshal.FinalReleaseComObject(_calculator);
            }

            _calculator = null;
            _activationScope.Dispose();
        }
        finally
        {
            _apartmentScope.Dispose();
        }
    }
}
```

### Recipe 3: Pinning `CultureInfo.InvariantCulture`, Win32 LCID (`SetThreadLocale`), and `MaxStackSize` on `SingleThreadedApartmentTaskSchedulerOptions`

Legacy VB6 (`CStr`/`CDbl`, `OLEAUT32!VarBstrFromR8`/`VarR8FromStr`), Borland Delphi (`SysUtils` format settings seeded from `kernel32!GetThreadLocale`), Compaq Visual Fortran, and Jet/DAO engines read **both** managed `CultureInfo.CurrentCulture` and the Win32 kernel thread LCID (`SetThreadLocale`), and often allocate large native stack frames on 32-bit `win-x86`:

```csharp
using System;
using System.Globalization;
using AdaskoTheBeAsT.Interop.COM;
using AdaskoTheBeAsT.Interop.Threading;

using var scheduler = new SingleThreadedApartmentTaskScheduler(
    new SingleThreadedApartmentTaskSchedulerOptions
    {
        ThreadName = "LegacyVb6Delphi-STA",
        MaxStackSize = 4 * 1024 * 1024, // 4 MB stack for deep 32-bit Fortran / Delphi / VB6 frames
        BeforeThreadStart = NanoServerOptions.RefreshStaState,
        ThreadCulture = CultureInfo.InvariantCulture,
        ThreadUICulture = CultureInfo.InvariantCulture,
        WindowsThreadLocaleId = 0x0809, // en-GB (or 0x0409 en-US) applied before CreateThreadScope
        ResetThreadCultureAndLocaleAfterWorkItem = true, // heals mid-call culture/LCID mutations
        PostWorkItemMessagePumpBatchSize = 64,
        FreeUnusedComLibrariesOnShutdown = true,
        ResetFloatingPointMaskAfterWorkItem = true,
    });
```

Because the scheduler applies `ThreadCulture`, `ThreadUICulture`, and `WindowsThreadLocaleId` on the STA worker **before** `CreateThreadScope` runs, in-process COM DLL startup (`DllMain`, Delphi unit initialization, VB6 runtime bootstrap) observes decimal-dot formatting even when the host OS runs under `pl-PL`, `de-DE`, `fr-FR`, or `tr-TR`. Enabling `ResetThreadCultureAndLocaleAfterWorkItem = true` restores both managed culture and Win32 LCID after `CreateThreadScope` and after each work item using fast equality checks so unmutated calls avoid redundant `SetThreadLocale` transitions.

## Practical notes

- Keep the COM DLL path and manifest path absolute and architecture-correct (`x86` vs `x64`).
- Pass synchronous delegates only. An async lambda or `Unwrap()` does not preserve STA affinity across awaits.
- Use `SingleThreadedApartmentTaskSchedulerExtensions.RunAsync(scheduler, action, cancellationToken)` when invoking `void` (`Action`) operations through `ISingleThreadedApartmentTaskScheduler` without returning dummy values.
- Use `RunCooperativeAsync((yield, token) => ..., timeout, callerToken)` on `ICooperativeStaTaskScheduler` or `SingleThreadedApartmentTaskScheduler` when loop iterations can observe cancellation, timeout, and shutdown between native calls.
- Release exclusively owned COM handles in `CreateThreadScope`'s `IDisposable.Dispose()` or in `finally` on the same STA thread. Do not call `FinalReleaseComObject` on shared RCWs.
- Enable `PostWorkItemMessagePumpBatchSize` (for example `64`) and `FreeUnusedComLibrariesOnShutdown = true` only when hosting in-process STA COM servers (such as VB6, Delphi VCL hidden windows, or Jet/ADO) that post deferred teardown messages or rely on `CoFreeUnusedLibrariesEx` before `CoUninitialize`. Both options preserve `WM_QUIT` for the scheduler's main loop.
- Pumping permits reentrant COM/window callbacks on the STA thread. Neither single-thread serialization nor a named mutex makes a non-reentrant COM component safe against arbitrary reentrancy.
- See the [compilable fake-component hosted service](../samples/StaService/README.md) for request cancellation, partial startup failure, reverse-order cleanup, and shutdown draining without third-party activation.
- For architectural rationale behind native STA apartment verification, owned thread scopes, bounded post-work-item pumping, Windows x86 floating-point hygiene, synchronous STA execution/apartment scopes, culture/Win32 locale hygiene, and `ValueTask`/cooperative timeouts, see [ADR 0005](adr/0005-native-sta-verification-and-thread-scope-lifecycle.md), [ADR 0006](adr/0006-post-work-message-pumping-and-com-library-cleanup.md), [ADR 0007](adr/0007-windows-x86-floating-point-hygiene.md), [ADR 0008](adr/0008-synchronous-sta-execution-and-apartment-scopes.md), [ADR 0009](adr/0009-sta-thread-culture-and-windows-locale-hygiene.md), and [ADR 0010](adr/0010-valuetask-and-cooperative-deadline-timeouts.md).
