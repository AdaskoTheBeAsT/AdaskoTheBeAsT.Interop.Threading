# Migration guide

Upgrade instructions for `AdaskoTheBeAsT.Interop.Threading`. Read the [changelog](CHANGELOG.md) for the full release history and the [README](README.md) for current usage examples.

**Current release: 4.0.0.** Review the framework requirements and behavior changes below before upgrading.

## Choose your upgrade path

| Starting version | Steps |
| --- | --- |
| 3.1.x | Follow [3.1 to 4.0](#from-31-to-40). |
| 3.0.x | Review [3.0 to 3.1](#from-30-to-31), then the 4.0 changes. |
| 2.x or earlier | Replace static scheduler calls using [2.x to 3.0](#from-2x-to-30), then review the later changes. |

Examples assume a Windows-targeted app and imports for `System`, `System.Threading`, `System.Threading.Tasks`, and `AdaskoTheBeAsT.Interop.Threading`, unless noted otherwise. Placeholder application methods stand for your own synchronous work.

## From 3.1 to 4.0

### 1. Check your target framework

| Target | Action |
| --- | --- |
| `net8.0`, `net9.0`, `net10.0` (including Windows-targeted consumers) | No target change required. Keep platform guards or Windows annotations for native calls. |
| `net472`, `net48`, `net481` | No target change required. |
| `net462`, `net47`, `net471` | Retarget to at least `net472` before upgrading. If that is not possible, remain on 3.1.x. |
| `netstandard2.0` | Not a package target since 3.0. Retarget the consuming library to a supported framework. |

For example:

```xml
<!-- Before -->
<TargetFramework>net462</TargetFramework>

<!-- After -->
<TargetFramework>net472</TargetFramework>
```

Installing a newer .NET Framework runtime does not retarget the project. Update the TFM and verify dependencies and deployment prerequisites too.

Building this repository requires the SDK selected by `global.json`; that is separate from the runtime required by a consuming application.

### 2. Handle initialization failure at construction

The scheduler constructor now waits for OLE initialization and throws if it fails. Code that handled initialization errors only around the first `RunAsync` must also handle scheduler creation, including DI resolution.

- Native initialization failures preserve the original exception, such as a `COMException` with its HRESULT.
- The public constructor allows 30 seconds for initialization; an initialization wait timeout throws `InvalidOperationException` and requests shutdown.
- Options are validated before native handles are allocated. Invalid `DefaultWorkItemTimeout` values and non-positive `MaximumPendingWorkItems` fail construction.

Do not depend on a partially initialized scheduler being available after construction fails.

### 3. Separate cancellation from termination

These remain different operations:

| Operation | What it guarantees |
| --- | --- |
| Stop awaiting a result | The caller can continue; native work may still be running. |
| Cancel pending work | The delegate will not start if cancellation wins before execution. |
| Cancel running work | A cooperative request, not forced interruption. |
| Await `Completion` | Observe actual worker termination or its failure. |

**New API:** use `RunCooperativeAsync` when the delegate can check cancellation.

Before, the delegate could see only the token you captured:

```csharp
await scheduler.RunAsync(
    (StaYield staYield) =>
    {
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ProcessItem(item);
            staYield.Occasionally();
        }
    },
    cancellationToken);
```

After, observe the effective token supplied by the scheduler:

```csharp
await scheduler.RunCooperativeAsync(
    (staYield, token) =>
    {
        foreach (var item in items)
        {
            token.ThrowIfCancellationRequested();
            ProcessItem(item);
            staYield.Occasionally();
        }
    },
    TimeSpan.FromSeconds(30),
    cancellationToken);
```

The token covers caller cancellation, timeout, and shutdown. The timeout starts at admission and includes queue time. A blocking `ProcessItem` still cannot be interrupted unless the operation itself supports cancellation.

Existing `RunAsync` signatures remain available. Custom implementations and mocks of `ISingleThreadedApartmentTaskScheduler` need no new members. To expose the new capabilities through DI, register and inject `ICooperativeStaTaskScheduler`. Its cooperative methods take an explicit timeout; the concrete scheduler additionally offers overloads that use the configured default.

### 4. Review shutdown and COM cleanup

`Dispose()` still waits for the STA worker and can block forever if native work never returns. `ShutdownAsync` is a new way to put a deadline on **waiting for termination**, not on the worker's lifetime:

```csharp
await scheduler.ShutdownAsync(
    TimeSpan.FromSeconds(5),
    CancellationToken.None);
```

After `TimeoutException`, do not assume the worker stopped. A `using` scope, `finally` block, or DI container that immediately calls `Dispose()` can still block. Decide how the application will retain and observe the scheduler, or isolate native work in another process when a hard stop is required.

For a long-lived COM component:

1. Stop accepting application requests.
2. Queue release of exclusively owned COM handles on their owning STA thread.
3. Give that cleanup operation an explicit `Timeout.InfiniteTimeSpan` and `CancellationToken.None` so a canceled host token or default item timeout does not discard it.
4. Bound the caller's cleanup wait separately with `TimeoutAfterAsync`, and observe eventual cleanup faults if the wait ends first.
5. Request scheduler shutdown only after cleanup has run. A blocked native call prevents cleanup from running too.

Do not submit cleanup after `Shutdown()`: admission is already closed. Do not force-release shared RCWs. See the [hosted-service sample](samples/StaService/README.md) for lifecycle and partial-startup cleanup.

Scheduler-owned `WM_QUIT` now also closes admission and initiates shutdown. If your code posts this message, treat it as a lifecycle event, not a harmless wake-up. `QuitExitCode` records its exit code on the concrete scheduler.

### 5. Update cancellation and exception expectations

Review tests that assert task status, token identity, or validation timing:

| Case | 4.0 behavior |
| --- | --- |
| Valid scheduling call with a pre-canceled caller token | Return a canceled task with that caller token; do not invoke the delegate, even after scheduler disposal. |
| Pending work canceled | Remove it from the queue and cancel with the effective linked token. |
| Running work returns after its effective token was canceled | Cancel the work-item task rather than return the result. |
| Delegate throws a matching-token `OperationCanceledException`, or throws OCE after effective-token cancellation | Cancel with the effective token. |
| Delegate throws an unrelated OCE while the effective token is not canceled | Preserve the original exception in a **faulted** task. |
| Delegate throws another exception | Preserve that fault, even if cancellation was requested. |
| Finite scheduler wait ends first | Caller cancellation wins over timeout when observed; otherwise return a `TimeoutException`. The item may still be running. |

Finite scheduler calls can complete from either the work item or its bounded wait. The wait uses the caller token, while the item uses the linked token. Do not assert that every cancellation contains the original caller token. Infinite-timeout scheduler calls return the item task, so noncooperative running work can delay caller cancellation.

For one-off `SingleThreadedApartmentTask` calls, OLE initialization and cleanup now belong to that invocation. `RunAsync` completes after the final bounded pump and OLE teardown. Post-execution cancellation can replace a successful return with cancellation; unrelated OCEs fault the task. Primary execution failures take precedence over cleanup failures. The timeout wrapper can still finish before the underlying operation and cleanup.

`TimeoutAfterAsync` supports both `Task` and `Task<T>`:

- Null source validation comes before timeout validation, including for completed tasks.
- Invalid arguments are reported through a faulted returned task, preserving asynchronous validation timing.
- With valid arguments, an already-completed source wins over a pre-canceled wait or zero timeout.
- Faulted OCEs remain faulted rather than being converted to canceled tasks.
- External source tasks remain yours to cancel, await, and fault-observe. The helper never cancels them.

Finite timeout budgets are **zero through `int.MaxValue - 1` milliseconds**. `Timeout.InfiniteTimeSpan` is the only negative sentinel. Scheduler scheduling validation throws synchronously before admission.

For the full race and token rules, see [ADR 0002](docs/adr/0002-sta-lifetime-and-cancellation-contracts.md). Concurrent events are not guaranteed to produce a single universal ordering.

### 6. Check message-pump waits

`StaYield.SpinUntil` now rejects a negative `checkEveryMs` before evaluating the condition, even if that condition would immediately return `true`. Pass a non-negative interval.

The new cancelable overloads let you stop cooperative waits:

```csharp
await scheduler.RunCooperativeAsync(
    (staYield, token) =>
    {
        if (!staYield.SpinUntil(
            () => IsReady(),
            TimeSpan.FromSeconds(2),
            token,
            checkEveryMs: 10))
        {
            throw new TimeoutException("The component was not ready.");
        }
    },
    TimeSpan.FromSeconds(5),
    cancellationToken);
```

Handle the Boolean result: the timed `SpinUntil` returns `false` on timeout, while cancellation throws. The old tokenless overloads remain available.

Pump batches now process at most 64 messages, allowing queued work a turn between batches. Borrowed pumps preserve `WM_QUIT` and its signed exit code. Pumping still permits reentrancy and cannot bound a blocking callback.

### 7. Migrate mutex policy deliberately

There is no required overload change. Existing overloads retain their ACL and abandonment defaults, but now validate names and timeouts explicitly.

For new or reviewed call sites, prefer `MutexExecutionOptions`:

| Policy | Legacy overloads | Options overload default |
| --- | --- | --- |
| Namespace | Global, unless `isGlobal: false` | Current Windows session |
| Creation ACL | Everyone FullControl | Current-user synchronize/modify |
| Abandonment | Warn and execute | Throw before delegate execution |
| Cancellation | No acquisition token | Optional token cancels acquisition |
| Name | Unqualified for global mode; explicit prefixes accepted with `isGlobal: false` | Always unqualified; select namespace with `IsGlobal` |

Example of an intentional **global, same-user** policy:

```csharp
var value = MutexHelper.RunInMutex(
    "MyApp.SharedState",
    new MutexExecutionOptions
    {
        IsGlobal = true,
        Timeout = TimeSpan.FromSeconds(5),
        FailOnAbandonedMutex = true,
    },
    () => ReadProtectedState(),
    cancellationToken);
```

This is **not** a drop-in security-policy replacement for legacy cross-user sharing. All cooperating processes must agree on the name, scope, and ACL. Supply a deliberate `MutexSecurity` for the intended principals when cross-user access is required.

Existing mutex ACLs are never rewritten. Switching overloads does not retroactively restrict an already-existing permissive mutex. Plan changes across processes rather than silently switching the namespace or creating a second lock.

If `FailOnAbandonedMutex = false`, validate or repair protected data inside the delegate. Ownership after abandonment is not proof that the data is consistent. The library releases any acquired ownership on the acquiring thread, including when acquisition is canceled before delegate invocation.

### Upgrade checklist

- [ ] Retarget removed frameworks and verify Windows deployment prerequisites.
- [ ] Handle scheduler initialization errors at creation/DI resolution.
- [ ] Keep all STA and mutex delegates synchronous.
- [ ] Use the effective cooperative token where work can stop.
- [ ] Verify queue-full handling if enabling `MaximumPendingWorkItems`.
- [ ] Test cancellation, timeout, unrelated OCE faults, and shutdown with active work.
- [ ] Release owned COM handles on the STA before closing scheduler admission.
- [ ] Review mutex scope/ACL/abandonment together across participating processes.
- [ ] Rerun validation for your actual target frameworks; do not reuse older matrix results.

## From 3.0 to 3.1

The modern package targets changed from `net8.0-windows`, `net9.0-windows`, and `net10.0-windows` to plain .NET TFMs. Windows-specific APIs gained platform annotations. Windows-targeted consumers continue to work without retargeting.

Portable callers now receive `CA1416` diagnostics when invoking Windows APIs without a guard. Guard the native branch rather than suppressing the warning globally:

```csharp
if (!OperatingSystem.IsWindows())
{
    throw new PlatformNotSupportedException("STA execution requires Windows.");
}

using var scheduler = new SingleThreadedApartmentTaskScheduler();
var apartment = await scheduler.RunAsync(
    () => Thread.CurrentThread.GetApartmentState(),
    CancellationToken.None);
```

This example is for modern .NET. `TaskExtension` and scheduler options remain portable; referencing a plain .NET package target does not make COM, STA, or mutex-security operations portable.

## From 2.x to 3.0

The scheduler changed from a static class to an instance implementing `ISingleThreadedApartmentTaskScheduler` and `IDisposable`. Each instance owns one reusable STA thread.

Before (2.x only, not valid against 3.x or 4.0):

```csharp
var first = SingleThreadedApartmentTaskScheduler.RunAsync(
    () => ComOperation1(), cancellationToken);
var second = SingleThreadedApartmentTaskScheduler.RunAsync(
    () => ComOperation2(), cancellationToken);
await Task.WhenAll(first, second);

SingleThreadedApartmentTaskScheduler.Shutdown();
```

After:

```csharp
using var scheduler = new SingleThreadedApartmentTaskScheduler();

var first = scheduler.RunAsync(() => ComOperation1(), cancellationToken);
var second = scheduler.RunAsync(() => ComOperation2(), cancellationToken);
await Task.WhenAll(first, second);
```

For application-wide use, register a singleton and inject the interface:

```csharp
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddSingleton<ISingleThreadedApartmentTaskScheduler>(
    _ => new SingleThreadedApartmentTaskScheduler(
        new SingleThreadedApartmentTaskSchedulerOptions { ThreadName = "App-STA" }));
```

This requires Microsoft.Extensions dependency-injection support. The container disposes the scheduler. Keep one instance per independent STA thread, not one per request. A static wrapper around an instance may reduce call-site edits, but it does not remove the need for explicit lifetime ownership.

Version 3.0 also removed `netstandard2.0` in favor of explicit .NET Framework assets. If upgrading directly to 4.0, use its current six-target matrix rather than the larger historical 3.x matrix.
