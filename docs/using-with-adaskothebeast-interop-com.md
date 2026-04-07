# Using `AdaskoTheBeAsT.Interop.Threading` with `AdaskoTheBeAsT.Interop.COM`

`AdaskoTheBeAsT.Interop.COM` and `AdaskoTheBeAsT.Interop.Threading` solve different parts of the same problem:

- `AdaskoTheBeAsT.Interop.COM` handles registration-free COM activation through manifests and activation contexts.
- `AdaskoTheBeAsT.Interop.Threading` gives you a guaranteed STA thread, timeout handling, and a reusable single-thread scheduler.

Together they are a good fit when you need to call legacy COM calculation components safely from modern async .NET code.

## When to combine them

Use both libraries when:

- the COM server must run on an STA thread;
- the COM DLL is deployed side-by-side with a manifest instead of being globally registered;
- you want to queue many COM calls onto one dedicated thread;
- you want cancellation and timeout support around COM calculations.

## One-off COM calculation with timeout

Use `SingleThreadedApartmentTask.RunWithTimeoutAsync` when the call is isolated and you do not need to reuse a dedicated STA scheduler.

```csharp
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
                    // Replace this with the coclass and method from your COM library.
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

## Queue many COM calculations on one STA thread

Use `SingleThreadedApartmentTaskScheduler.RunAsync` when many callers should share one dedicated STA thread and execute sequentially.

```csharp
using AdaskoTheBeAsT.Interop.COM;
using AdaskoTheBeAsT.Interop.Threading;

public sealed class ScheduledComCalculator
{
    private readonly string _comDllPath;
    private readonly string _manifestPath;

    public ScheduledComCalculator(string comDllPath, string manifestPath)
    {
        _comDllPath = comDllPath;
        _manifestPath = manifestPath;
    }

    public Task<decimal> AddAsync(decimal left, decimal right, CancellationToken cancellationToken)
        => SingleThreadedApartmentTaskScheduler.RunAsync(
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

    public Task<decimal> MultiplyAsync(decimal left, decimal right, CancellationToken cancellationToken)
        => SingleThreadedApartmentTaskScheduler.RunAsync(
            () =>
            {
                decimal result = default;

                var execution = Executor.Execute(_comDllPath, _manifestPath, () =>
                {
                    var calculator = new LegacyCalculator.CalculatorClass();
                    result = calculator.Multiply(left, right);
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

Calls such as:

```csharp
var addTask = calculator.AddAsync(10m, 5m, cancellationToken);
var multiplyTask = calculator.MultiplyAsync(7m, 6m, cancellationToken);

await Task.WhenAll(addTask, multiplyTask);
```

will be queued and executed one-by-one on the scheduler's single STA thread.

## Batch many calculations in one scheduled operation

If the COM component is sensitive to thread affinity or you want one logical batch to stay on the same STA execution block, put the whole batch into one scheduled operation.

```csharp
using AdaskoTheBeAsT.Interop.COM;
using AdaskoTheBeAsT.Interop.Threading;

public Task<IReadOnlyList<decimal>> CalculateBatchAsync(
    IReadOnlyList<(decimal Left, decimal Right)> inputs,
    CancellationToken cancellationToken)
    => SingleThreadedApartmentTaskScheduler.RunAsync(
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
```

`StaYield` is useful when the COM work is long-running and you still want the STA message pump to stay responsive.

## Why this composition works well

- `Executor.Execute(...)` activates the COM manifest context.
- `SingleThreadedApartmentTask` guarantees a dedicated STA thread for one call.
- `SingleThreadedApartmentTaskScheduler` guarantees serialized execution on one reusable STA thread.
- `TimeoutAfterAsync` / `RunWithTimeoutAsync` lets you put an upper bound on COM calls that might stall.

## Practical notes

- Replace `LegacyCalculator.CalculatorClass` and its methods with the actual coclass generated for your COM component.
- Keep the COM DLL path and manifest path absolute and architecture-correct (`x86` vs `x64`).
- `Executor.Execute(...)` returns `Result`, so capture the COM return value in a local variable inside the callback and then validate `Result.Success`.
- If several COM calls must share the same logical STA workflow, keep them in the same scheduled operation instead of splitting them across unrelated tasks.
