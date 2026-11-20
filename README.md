# 🧵 AdaskoTheBeAsT.Interop.Threading

> 🪟 A friendly Windows threading toolbox for STA/COM work, named mutexes, and task timeouts. Less plumbing. More app code.

[![NuGet](https://img.shields.io/nuget/v/AdaskoTheBeAsT.Interop.Threading.svg?logo=nuget)](https://www.nuget.org/packages/AdaskoTheBeAsT.Interop.Threading/)
[![NuGet downloads](https://img.shields.io/nuget/dt/AdaskoTheBeAsT.Interop.Threading.svg?logo=nuget&label=downloads)](https://www.nuget.org/packages/AdaskoTheBeAsT.Interop.Threading/)
[![CI](https://img.shields.io/github/actions/workflow/status/AdaskoTheBeAsT/AdaskoTheBeAsT.Interop.Threading/ci.yml?branch=main&logo=github&label=CI)](https://github.com/AdaskoTheBeAsT/AdaskoTheBeAsT.Interop.Threading/actions)
[![Quality Gate](https://sonarcloud.io/api/project_badges/measure?project=AdaskoTheBeAsT_AdaskoTheBeAsT.Interop.Threading&metric=alert_status)](https://sonarcloud.io/summary/new_code?id=AdaskoTheBeAsT_AdaskoTheBeAsT.Interop.Threading)
[![Coverage](https://sonarcloud.io/api/project_badges/measure?project=AdaskoTheBeAsT_AdaskoTheBeAsT.Interop.Threading&metric=coverage)](https://sonarcloud.io/component_measures?id=AdaskoTheBeAsT_AdaskoTheBeAsT.Interop.Threading&metric=coverage)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://github.com/AdaskoTheBeAsT/AdaskoTheBeAsT.Interop.Threading/blob/main/LICENSE)

> ✨ **Current release: 5.0.0.** Upgrading from an earlier version? Start with the [migration guide](https://github.com/AdaskoTheBeAsT/AdaskoTheBeAsT.Interop.Threading/blob/main/MIGRATION.md).

## 👋 Hello, threading friend

Your COM component wants its own STA thread. Your app wants responsive callers. You probably don't want to write another message pump.

That's where this library comes in: run synchronous interop work on dedicated STA threads, coordinate access across processes, and put a deadline on waiting for results.

### ✨ Why you'll like it

- 🧵 **A home for your COM objects.** Reuse one STA thread, or spin up a temporary one for an isolated call.
- 🧩 **Fits your app, not the other way around.** Instance-based schedulers, options, and interfaces for DI and tests.
- ⏱️ **Know why a wait ended.** Distinguish a timeout from caller cancellation without pretending native work was terminated.
- 🔒 **Coordinate across processes.** Named mutexes with explicit scope, permissions, and abandonment policy in 4.0.
- 🔎 **See what's happening.** Source Link for stepping into the library, plus opt-in scheduler diagnostics in 4.0.

---

## 📚 Contents

- [📦 Install and compatibility](#-install-and-compatibility)
- [🎯 Choose an API](#-choose-an-api)
- [🚀 Quick start](#-quick-start)
- [🔄 Cooperative cancellation and message pumping](#-cooperative-cancellation-and-message-pumping)
- [🔧 Scheduler options and lifetime](#-scheduler-options-and-lifetime)
- [🧭 Thread affinity, STA apartment scopes, and locale hygiene](#-thread-affinity-sta-apartment-scopes-and-locale-hygiene)
- [🔒 Named mutexes](#-named-mutexes)
- [⏳ Task timeouts](#-task-timeouts)
- [🤝 COM integration](#-com-integration)
- [🧭 Migration guide](#-migration-guide)
- [📋 Changelog](#-changelog)
- [🧪 Development and validation](#-development-and-validation)

## 📦 Install and compatibility

Grab the published package:

```shell
dotnet add package AdaskoTheBeAsT.Interop.Threading
```

**Five targets, one package:**

| Runtime family | Package targets in 5.0 |
| --- | --- |
| .NET | `net11.0`, `net10.0` |
| .NET Framework | `net481`, `net48`, `net472` |

Version 5.0 adds `net11.0` and removes `net9.0` and `net8.0`. The .NET Framework minimum remains **4.7.2**.

- 🪟 **Windows required:** STA execution (`SingleThreadedApartmentTaskScheduler`, `SingleThreadedApartmentTask`, `StaApartment`), Win32 LCID scopes (`ThreadCultureScope.Enter(..., windowsThreadLocaleId)`), message pumping (`StaYield`), and named mutexes (`MutexHelper`).
- 🌍 **Portable:** `TaskExtension` (`Task`, `ValueTask` on `.NET 10`/`11`, and cooperative linked-deadline overloads), `ThreadAffinity`, `ThreadCultureScope.Enter(culture, uiCulture)`, `SingleThreadedApartmentTaskSchedulerOptions`, `SingleThreadedApartmentTaskOptions`, and `StaExecutionMode`.
- 📦 **Zero added NuGet dependencies:** `System.Threading.AccessControl` remains the sole external dependency across all five targets.
- On modern .NET, Windows-specific APIs carry `[SupportedOSPlatform("windows")]`. Use a Windows target such as `net11.0-windows` or `net10.0-windows`, or guard native calls with `OperatingSystem.IsWindows()`.
- 🔎 Symbols ship as `.snupkg` with Source Link. Step into the library when you need to see what happens underneath.

Examples use modern C# syntax. Native examples assume a Windows-targeted application. Besides the library namespace, later snippets use `System`, `System.Globalization`, `System.IO`, `System.Threading`, and `System.Threading.Tasks` as needed.

## 🎯 Choose an API

Pick the tool that matches the job. No need to adopt the whole toolbox.

| You need to… | Use |
| --- | --- |
| Reuse a COM object on one STA thread across many calls | `SingleThreadedApartmentTaskScheduler` (plus `Action` extensions) |
| Run an isolated async or synchronous operation on STA | `SingleThreadedApartmentTask.RunAsync` / `RunWithTimeoutAsync` / `Run` (`StaExecutionMode`) |
| Guard thread ownership or lease COM/OLE on an external STA thread | `ThreadAffinity.Capture()` and `StaApartment.EnterScope()` |
| Pin and restore `CultureInfo` and Win32 thread LCID (`SetThreadLocale`) | Scheduler locale options or `ThreadCultureScope.Enter(...)` |
| Check cancellation and pump messages inside synchronous STA work | `RunCooperativeAsync` (4.0) with `StaYield` |
| Serialize synchronous work across processes (including STA work) | `MutexHelper.RunInMutex` (`Func<T>` or `Action`) |
| Bound waiting or enforce cooperative deadlines on `Task` / `ValueTask` | `TaskExtension.TimeoutAfterAsync` |

### ⚠️ Two rules worth keeping close

1. Pass **synchronous delegates only** to STA and mutex APIs. Async lambdas, `async void`, nested tasks, and `Unwrap()` do not preserve STA affinity or mutex ownership across awaits.
2. A timeout stops the caller's wait, **not a blocked native call**. Cancellation is cooperative. If native work never returns, scheduler disposal can wait indefinitely. Use a separate process when you need hard termination.

## 🚀 Quick start

Let's give your work an STA thread. Reuse one scheduler for calls that must run serially on that same thread:

```csharp
using System;
using System.Threading;
using AdaskoTheBeAsT.Interop.Threading;

using var scheduler = new SingleThreadedApartmentTaskScheduler();

var apartment = await scheduler.RunAsync(
    () => Thread.CurrentThread.GetApartmentState(),
    CancellationToken.None);

Console.WriteLine(apartment); // STA
```

Each scheduler owns one background thread and initializes OLE on it. Create one scheduler per independent STA thread you need, not one per request. Disposal requests shutdown and waits for the worker to exit.

Just visiting STA land? For an isolated operation, use `RunAsync`, `RunWithTimeoutAsync`, or synchronous `Run`:

```csharp
var apartment = await SingleThreadedApartmentTask.RunWithTimeoutAsync(
    TimeSpan.FromSeconds(5),
    () => Thread.CurrentThread.GetApartmentState(),
    CancellationToken.None);

// Synchronous STA execution (safe to compose directly inside MutexHelper.RunInMutex):
SingleThreadedApartmentTask.Run(
    () => DoStaWork(),
    StaExecutionMode.DedicatedThread, // or ReuseCurrentStaThread to run inline when already on STA
    CancellationToken.None);
```

Both `SingleThreadedApartmentTask` and `ISingleThreadedApartmentTaskScheduler` support `Action` (`void`) delegates alongside `Func<T>`. Synchronous `SingleThreadedApartmentTask.Run` **always joins** any dedicated STA thread it starts before returning (so enclosing `MutexHelper.RunInMutex` scopes never release a cross-process mutex while native work is still running) and rethrows worker exceptions via `ExceptionDispatchInfo` with their original stack trace intact:

- `StaExecutionMode.DedicatedThread` (default): starts one OLE-initialized background STA thread, verifies native STA apartment membership, runs a bounded final message pump, and uninitializes COM/OLE before returning.
- `StaExecutionMode.ReuseCurrentStaThread`: when the caller thread is already `ApartmentState.STA`, runs inline under `StaApartment.EnterScope(initializeOle: true)` without spawning a thread and **without** draining any pending `WM_QUIT` on the caller's message queue; falls back to `DedicatedThread` when called from an MTA thread.

For configurable one-off `Run` and `RunAsync` calls, pass `SingleThreadedApartmentTaskOptions`:

| Option | Default | Meaning |
| --- | --- | --- |
| `ThreadName` | `"STA Task Thread"` | Background thread name when running on a dedicated thread. |
| `MaxStackSize` | `0` (runtime default) | Non-negative stack reservation in bytes passed to `new Thread(start, maxStackSize)` when positive. |
| `ExecutionMode` | `DedicatedThread` | Choose `DedicatedThread` or `ReuseCurrentStaThread` (`RunAsync` always uses a dedicated background thread so callers are not blocked synchronously). |
| `BeforeThreadStart` | `null` | Synchronous callback invoked before starting a dedicated thread (for example `NanoServerOptions.RefreshStaState`). |
| `VerifyNativeStaApartment` | `true` | Verify native STA apartment state via `CoGetApartmentType`, recovering `CO_E_NOTINITIALIZED` with balanced `CoInitializeEx(STA)`. |
| `ThreadCulture` / `ThreadUICulture` | `null` | Optional `CultureInfo` applied for the invocation (restored automatically in inline reuse mode). |
| `WindowsThreadLocaleId` | `null` | Optional positive Win32 LCID applied via `kernel32!SetThreadLocale` (restored automatically in inline reuse mode). |
| `ResetFloatingPointMaskBeforeExit` | `false` | On Windows `x86`, clear pending x87/SSE status flags and restore standard exception masks (`_MCW_EM`) after the delegate runs. |

Create, use, and release any STA-bound COM object inside that invocation. Do not return a COM object for use on the caller's thread.

## 🔄 Cooperative cancellation and message pumping

Long loop? Give cancellation a chance to be heard, and keep those Windows messages moving.

**New in 4.0:** `RunCooperativeAsync` supplies a token linked to caller cancellation, the work-item timeout, and scheduler shutdown.

```csharp
using var scheduler = new SingleThreadedApartmentTaskScheduler();

var total = await scheduler.RunCooperativeAsync(
    (staYield, token) =>
    {
        var sum = 0;
        for (var i = 0; i < 10_000; i++)
        {
            token.ThrowIfCancellationRequested();
            sum += i;
            staYield.Occasionally();
        }

        return sum;
    },
    TimeSpan.FromSeconds(3),
    CancellationToken.None);
```

Use the supplied token, not just a captured caller token, so work can react to timeout and shutdown too. Existing `RunAsync` delegates keep their signatures and are checked before and after execution.

Think of `StaYield` as a pit stop for synchronous work. It runs on the current STA thread:

| Method | Purpose |
| --- | --- |
| `Occasionally()` | Pump a bounded batch of messages when the interval has elapsed (15 ms by default). |
| `Sleep(milliseconds, token)` | Wait while pumping and checking cancellation (4.0). |
| `SpinUntil(condition, timeout, token)` | Pump while polling; return `false` on timeout and throw on cancellation (4.0). |

The existing `Sleep(milliseconds)` and `SpinUntil(condition, checkEveryMs)` overloads remain available without cancellation.

> ⚠️ Pumping permits **reentrant callbacks**. Avoid holding arbitrary locks across pumped calls. A bounded message count cannot prevent a single callback or COM call from blocking.

## 🔧 Scheduler options and lifetime

Name your thread, set a budget, and decide how much work you're willing to queue:

```csharp
using var scheduler = new SingleThreadedApartmentTaskScheduler(
    new SingleThreadedApartmentTaskSchedulerOptions
    {
        ThreadName = "App-STA",
        DefaultWorkItemTimeout = TimeSpan.FromSeconds(30),
        MaximumPendingWorkItems = 100,
        EnableDiagnostics = true,
    });
```

| Option | Default | Meaning |
| --- | --- | --- |
| `ThreadName` | `"STA Task Scheduler Thread"` | Background thread name. |
| `DefaultWorkItemTimeout` | `Timeout.InfiniteTimeSpan` | Budget used unless the call supplies a timeout. Includes queue time. |
| `MaximumPendingWorkItems` (4.0) | `null` (unlimited) | Positive queue limit, excluding the active delegate. Full queues return a task faulted with `InvalidOperationException`; admission does not wait for space. |
| `EnableDiagnostics` (4.0) | `false` | Enable the `AdaskoTheBeAsT-Interop-Threading` EventSource. Attach a listener to collect events. |
| `BeforeThreadStart` (5.0) | `null` | Synchronous callback invoked on the constructing thread before `Thread.Start()` triggers CoreCLR's native STA initialization (for example `NanoServerOptions.RefreshStaState`). |
| `VerifyNativeStaApartment` (5.0) | `true` | Verify native COM apartment state via `CoGetApartmentType` after `OleInitialize`, recovering `CO_E_NOTINITIALIZED` with balanced `CoInitializeEx(STA)` and failing construction if the worker is in MTA. |
| `CreateThreadScope` (5.0) | `null` | Optional `Func<IDisposable?>` invoked once on the owned STA thread before work runs; its `IDisposable` is disposed once on that exact STA thread during shutdown. |
| `PostWorkItemMessagePumpBatchSize` (5.0) | `0` (disabled) | Non-negative cap on pending messages pumped (`preserveQuit: true`) immediately after each executed work item and during final shutdown drain. |
| `FreeUnusedComLibrariesOnShutdown` (5.0) | `false` | When `true`, invoke `CoFreeUnusedLibrariesEx(0, 0)` plus one bounded message-pump pass after `CreateThreadScope` disposal and before `CoUninitialize` / `OleUninitialize`. |
| `ResetFloatingPointMaskAfterWorkItem` (5.0) | `false` | On Windows `x86` only, clear pending x87/SSE status flags (`_clearfp`) and restore standard exception masks (`_MCW_EM` via `__control87_2`) after `CreateThreadScope` and each work item without altering rounding or precision modes. |
| `ThreadCulture` / `ThreadUICulture` (5.0) | `null` | Optional `CultureInfo` applied on the STA worker thread **before** `CreateThreadScope` and work items run. |
| `WindowsThreadLocaleId` (5.0) | `null` | Optional positive Win32 LCID (for example `0x0409` `en-US` or `0x0809` `en-GB`) applied via `kernel32!SetThreadLocale` before `CreateThreadScope` so VB6/`OLEAUT32`/Delphi/Jet servers use deterministic decimal formatting. |
| `ResetThreadCultureAndLocaleAfterWorkItem` (5.0) | `false` | When `true`, restore mutated `CurrentCulture`, `CurrentUICulture`, and Win32 thread LCID after `CreateThreadScope` and after each work item using fast equality checks. |
| `MaxStackSize` (5.0) | `0` (runtime default) | Non-negative stack reservation in bytes passed to `new Thread(start, maxStackSize)` when positive (useful for deep 32-bit Fortran/Delphi/VB6 native stack frames). |

### 🛑 Shutdown: a request, not an eject button

- `Shutdown()` closes admission, cancels pending work, and requests cooperative cancellation of running work. It does not wait for worker exit.
- `ShutdownAsync(timeout, token)` (4.0) requests shutdown and bounds **only the termination wait**.
- `Completion` (4.0) reports actual worker exit or its terminal failure.
- `Dispose()` joins the worker when called from another thread, potentially indefinitely.

For an owner-managed scheduler, a bounded shutdown wait looks like this:

```csharp
await scheduler.ShutdownAsync(
    TimeSpan.FromSeconds(5),
    CancellationToken.None);
```

If this times out, the worker may still be alive. A subsequent `Dispose()`, including automatic `using` or DI disposal, is still unbounded. Keep ownership of the scheduler and define an application-level shutdown policy; do not start a replacement worker against the same COM state while the original is running.

### 🧩 Dependency injection

Register one instance per STA thread and inject `ISingleThreadedApartmentTaskScheduler`:

```csharp
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddSingleton<ISingleThreadedApartmentTaskScheduler>(
    _ => new SingleThreadedApartmentTaskScheduler(
        new SingleThreadedApartmentTaskSchedulerOptions { ThreadName = "App-STA" }));
```

This example requires the `Microsoft.Extensions.DependencyInjection` package or a host that provides it. The container owns disposal. Complete COM cleanup before the scheduler is shut down or disposed.

The original interface is unchanged in 4.0. Inject `ICooperativeStaTaskScheduler` instead if you need cooperative scheduling, `ShutdownAsync`, or `Completion`. The concrete scheduler also exposes `PendingWorkItemCount` and `QuitExitCode`.

Diagnostics report scheduler/work IDs, state, pending counts, and durations without arguments, results, or exception messages. Events are asynchronous and can arrive out of order; correlate IDs rather than delivery order. See [the lifetime contracts](https://github.com/AdaskoTheBeAsT/AdaskoTheBeAsT.Interop.Threading/blob/main/docs/adr/0002-sta-lifetime-and-cancellation-contracts.md).

## 🧭 Thread affinity, STA apartment scopes, and locale hygiene

Hosting STA COM sessions on external STA worker pools (such as `ExecutionWorkerPool<TSession>` with `UseStaThread = true`) or `[StaFact]` test threads? Use the standalone zero-dependency guards directly:

```csharp
var affinity = ThreadAffinity.Capture();
affinity.ThrowIfWrongThread(ownerName: "CalculatorSession");

using var apartment = StaApartment.EnterScope(initializeOle: true);
using var locale = ThreadCultureScope.Enter(
    CultureInfo.InvariantCulture,
    CultureInfo.InvariantCulture,
    windowsThreadLocaleId: 0x0809);
```

- **`ThreadAffinity`** (portable `readonly struct`): inline 4-byte thread-ID guard; `default(ThreadAffinity)` fails closed with `InvalidOperationException` instead of matching any managed thread.
- **`StaApartment` & `StaApartmentScope`** (Windows): `IsCurrentThreadSta`, `VerifyCurrentThreadSta`, and `EnterScope(initializeOle)` verify STA state via `CoGetApartmentType` and hold a balanced, idempotent, thread-affinitized `CoInitializeEx(COINIT_APARTMENTTHREADED)` (+ optional `OleInitialize`) lease—ensuring `"OleMainThreadWndClass"` is registered on CoreCLR `RoInitialize` STA worker threads before contended waits.
- **`ThreadCultureScope`**: portable `Enter(culture, uiCulture)` and Windows `Enter(culture, uiCulture, windowsThreadLocaleId)` pin and restore managed cultures and `kernel32!SetThreadLocale` on the owning thread.

## 🔒 Named mutexes

Sometimes `lock` isn't enough: another process wants the same resource. A named mutex lets cooperating processes take turns.

**New in 4.0:** prefer explicit mutex options for new code:

```csharp
var contents = MutexHelper.RunInMutex(
    "MyApp.SharedState",
    new MutexExecutionOptions
    {
        Timeout = TimeSpan.FromSeconds(2),
    },
    () => File.ReadAllText("shared-state.json"),
    CancellationToken.None);
```

By default, this overload uses session scope, grants the current user synchronize/modify rights at creation, and throws `AbandonedMutexException` rather than executing the delegate after abandonment.

- `Timeout` and the token limit **acquisition**, not execution of the delegate.
- Set `IsGlobal = true` for machine-wide naming. Pass an unqualified name, without `Global\` or `Local\`.
- Supply `Security` explicitly for cross-user sharing. Opening an existing mutex never rewrites its ACL.
- Set `FailOnAbandonedMutex = false` only when your delegate can validate or repair protected state before use.
- Mutex ownership and release stay on the acquiring thread. Do not pass an async delegate; all four `MutexHelper.RunInMutex` signatures accept both `Func<T>` and `Action` (`void`) synchronous delegates, and compose directly with `SingleThreadedApartmentTask.Run(...)`.

**Legacy overloads retain their existing policy:** global scope by default, Everyone FullControl at creation, and a warning followed by execution after abandonment. Switching to options changes defaults; see the migration guide before updating cooperating processes.

> 🛡️ A mutex coordinates access; it doesn't authorize callers or prevent name squatting. Scope and ACLs must match across participating processes.

## ⏳ Task timeouts

Your caller has a deadline, even when the operation doesn't.

`TimeoutAfterAsync` works with `Task<T>`, non-generic `Task` (4.0), cooperative linked-deadline `Func<CancellationToken, Task>` delegates (5.0), and—on `net10.0` and `net11.0`—pooled `ValueTask` / `ValueTask<T>` (5.0):

```csharp
public static async Task<int> WaitForResultAsync(
    Task<int> operation,
    CancellationToken cancellationToken)
{
    return await operation.TimeoutAfterAsync(
        TimeSpan.FromSeconds(5),
        cancellationToken);
}

// Cooperative deadline: passes a linked token canceled on timeout while surfacing TimeoutException
var computed = await TaskExtension.TimeoutAfterAsync(
    async linkedToken =>
    {
        await Task.Delay(100, linkedToken);
        return 42;
    },
    TimeSpan.FromSeconds(2),
    CancellationToken.None);
```

- Expired wait or deadline: `TimeoutException` (even when a cooperative delegate cancels via the linked deadline token).
- Caller cancels the wait: `OperationCanceledException` carrying the caller token.
- Source wins: preserve its result, fault (including unrelated faulted `OperationCanceledException`), or cancellation.
- On `net10.0` and `net11.0`, `ValueTask` and `ValueTask<T>` overloads consume any underlying `IValueTaskSource` **strictly once**, allocate **zero heap bytes** when already completed successfully, and observe late faults if abandoned.
- An already-completed source wins over zero timeout or a pre-canceled wait, after argument validation.

> 💡 For external `Task` / `ValueTask` instances, a timeout means “I stopped waiting,” not “the work stopped.” Those overloads never cancel the source task; use the `Func<CancellationToken, Task>` / `Func<CancellationToken, ValueTask>` overloads when the operation accepts a cooperative deadline token.

Finite timeout values range from zero through `int.MaxValue - 1` milliseconds. Use `Timeout.InfiniteTimeSpan` for no deadline. See [the migration guide](https://github.com/AdaskoTheBeAsT/AdaskoTheBeAsT.Interop.Threading/blob/main/MIGRATION.md) for validation and cancellation precedence.

## 🤝 COM integration

Better together: `AdaskoTheBeAsT.Interop.COM` handles registration-free activation; this library gives the work an STA thread to run on.

- Use one-off STA execution when creation, use, and release fit inside one invocation.
- Use a reusable scheduler when a COM object must stay alive across requests. Create it on that scheduler, route every call through the same instance, and release it there before shutdown.
- Use separate schedulers for independent components that must run concurrently.

See the [COM integration guide](https://github.com/AdaskoTheBeAsT/AdaskoTheBeAsT.Interop.Threading/blob/main/docs/using-with-adaskothebeast-interop-com.md) and [compiled hosted-service sample](https://github.com/AdaskoTheBeAsT/AdaskoTheBeAsT.Interop.Threading/blob/main/samples/StaService/README.md). The sample uses a fake thread-affine component and covers cancellation, partial startup failure, and reverse-order cleanup. It does not validate third-party COM behavior.

## 🧭 Migration guide

Upgrading? Start here before changing call sites.

See [MIGRATION.md](https://github.com/AdaskoTheBeAsT/AdaskoTheBeAsT.Interop.Threading/blob/main/MIGRATION.md) for **4.x to 5.0**, **3.1 to 4.0**, **3.0 to 3.1**, and **2.x to instance-based scheduling**.

## 📋 Changelog

What's new, what changed, and what might need your attention: [CHANGELOG.md](https://github.com/AdaskoTheBeAsT/AdaskoTheBeAsT.Interop.Threading/blob/main/CHANGELOG.md) tracks release history and notable changes.

## 🧪 Development and validation

Use PowerShell 7 and the .NET SDK selected by `global.json`. Read [validation notes and limitations](https://github.com/AdaskoTheBeAsT/AdaskoTheBeAsT.Interop.Threading/blob/main/docs/validation.md) before running `./scripts/Invoke-Validation.ps1`.

Additional references:

- [ADR 0001: Timeout cancellation and scheduler dispatch](https://github.com/AdaskoTheBeAsT/AdaskoTheBeAsT.Interop.Threading/blob/main/docs/adr/0001-hardening-timeouts-and-sta-scheduler.md)
- [ADR 0002: STA lifetime and cancellation contracts](https://github.com/AdaskoTheBeAsT/AdaskoTheBeAsT.Interop.Threading/blob/main/docs/adr/0002-sta-lifetime-and-cancellation-contracts.md)
- [ADR 0003: Analyzer baseline, native pointer types, and lockfile removal](https://github.com/AdaskoTheBeAsT/AdaskoTheBeAsT.Interop.Threading/blob/main/docs/adr/0003-analyzer-baseline-and-lockfile-removal.md)
- [ADR 0004: .NET 11 target baseline, removed frameworks, and major version 5.0.0](https://github.com/AdaskoTheBeAsT/AdaskoTheBeAsT.Interop.Threading/blob/main/docs/adr/0004-net11-target-baseline-and-major-version-5.md)
- [ADR 0005: Native COM STA apartment verification, pre-start hook, and owned thread scopes](https://github.com/AdaskoTheBeAsT/AdaskoTheBeAsT.Interop.Threading/blob/main/docs/adr/0005-native-sta-verification-and-thread-scope-lifecycle.md)
- [ADR 0006: Opt-in bounded post-work-item message pumping and shutdown COM library cleanup](https://github.com/AdaskoTheBeAsT/AdaskoTheBeAsT.Interop.Threading/blob/main/docs/adr/0006-post-work-message-pumping-and-com-library-cleanup.md)
- [ADR 0007: Opt-in Windows x86 floating-point exception-mask hygiene](https://github.com/AdaskoTheBeAsT/AdaskoTheBeAsT.Interop.Threading/blob/main/docs/adr/0007-windows-x86-floating-point-hygiene.md)
- [ADR 0008: Synchronous STA execution, apartment verification parity, and standalone apartment scopes](https://github.com/AdaskoTheBeAsT/AdaskoTheBeAsT.Interop.Threading/blob/main/docs/adr/0008-synchronous-sta-execution-and-apartment-scopes.md)
- [ADR 0009: Managed STA thread culture and Win32 thread locale (`SetThreadLocale`) hygiene](https://github.com/AdaskoTheBeAsT/AdaskoTheBeAsT.Interop.Threading/blob/main/docs/adr/0009-sta-thread-culture-and-windows-locale-hygiene.md)
- [ADR 0010: `ValueTask` single-consumption timeouts, cooperative linked deadlines, and STA thread stack sizing](https://github.com/AdaskoTheBeAsT/AdaskoTheBeAsT.Interop.Threading/blob/main/docs/adr/0010-valuetask-and-cooperative-deadline-timeouts.md)
- [Benchmarks](https://github.com/AdaskoTheBeAsT/AdaskoTheBeAsT.Interop.Threading/blob/main/benchmarks/README.md)

### 💬 Found an edge case?

Threading has plenty of them. Bug reports, focused fixes, and clearer examples are welcome. Include regression tests for behavior changes and update the changelog when relevant.

## 📝 License

[MIT](https://github.com/AdaskoTheBeAsT/AdaskoTheBeAsT.Interop.Threading/blob/main/LICENSE). Happy threading! 🧵
