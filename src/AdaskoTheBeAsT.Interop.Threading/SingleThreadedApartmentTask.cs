using System;
using System.Globalization;
using System.Runtime.ExceptionServices;
#if NET8_0_OR_GREATER
using System.Runtime.Versioning;
#endif
using System.Threading;
using System.Threading.Tasks;

namespace AdaskoTheBeAsT.Interop.Threading;

/// <summary>
/// Runs synchronous delegates in a Windows single-threaded apartment (STA) synchronously or asynchronously.
/// Dedicated-thread calls own one OLE-initialized background STA thread, verify native STA membership,
/// perform a bounded final message pump, and uninitialize COM/OLE before completing.
/// </summary>
/// <remarks>Delegates must be synchronous. Neither async lambdas nor Unwrap preserve STA affinity across awaits.</remarks>
#if NET8_0_OR_GREATER
[SupportedOSPlatform("windows")]
#endif
public static class SingleThreadedApartmentTask
{
    /// <summary>
    /// Synchronously executes an action on a dedicated background STA thread.
    /// </summary>
    /// <param name="action">The synchronous action to execute on the STA thread.</param>
    /// <param name="cancellationToken">A token checked before starting and around execution; any started STA thread is always joined before returning.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="action"/> is <see langword="null"/>.</exception>
    public static void Run(Action action, CancellationToken cancellationToken = default)
    {
        ValidateDelegate(action, nameof(action));
        _ = Run(WrapAction(action), options: null, new StaPlatform(), cancellationToken);
    }

    /// <summary>
    /// Synchronously executes an action on a dedicated background STA thread and provides a <see cref="StaYield"/> helper.
    /// </summary>
    /// <param name="action">The synchronous action to execute on the STA thread.</param>
    /// <param name="cancellationToken">A token checked before starting and around execution; any started STA thread is always joined before returning.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="action"/> is <see langword="null"/>.</exception>
    public static void Run(Action<StaYield> action, CancellationToken cancellationToken = default)
    {
        ValidateDelegate(action, nameof(action));
        _ = Run(WrapYieldAction(action), options: null, new StaPlatform(), cancellationToken);
    }

    /// <summary>
    /// Synchronously executes a function on a dedicated background STA thread and returns its result.
    /// </summary>
    /// <typeparam name="T">The type returned by <paramref name="func"/>.</typeparam>
    /// <param name="func">The synchronous function to execute on the STA thread.</param>
    /// <param name="cancellationToken">A token checked before starting and around execution; any started STA thread is always joined before returning.</param>
    /// <returns>The value returned by <paramref name="func"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="func"/> is <see langword="null"/>.</exception>
    public static T Run<T>(Func<T> func, CancellationToken cancellationToken = default)
    {
        ValidateDelegate(func, nameof(func));
        return Run(func, options: null, new StaPlatform(), cancellationToken);
    }

    /// <summary>
    /// Synchronously executes a function on a dedicated background STA thread and provides a <see cref="StaYield"/> helper.
    /// </summary>
    /// <typeparam name="T">The type returned by <paramref name="func"/>.</typeparam>
    /// <param name="func">The synchronous function to execute on the STA thread.</param>
    /// <param name="cancellationToken">A token checked before starting and around execution; any started STA thread is always joined before returning.</param>
    /// <returns>The value returned by <paramref name="func"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="func"/> is <see langword="null"/>.</exception>
    public static T Run<T>(Func<StaYield, T> func, CancellationToken cancellationToken = default)
    {
        ValidateDelegate(func, nameof(func));
        return Run(WrapYieldFunc(func), options: null, new StaPlatform(), cancellationToken);
    }

    /// <summary>
    /// Synchronously executes an action in an STA apartment using the specified <see cref="StaExecutionMode"/>.
    /// </summary>
    /// <param name="action">The synchronous action to execute.</param>
    /// <param name="mode">Whether to always spawn a dedicated STA thread or reuse the calling thread when already STA.</param>
    /// <param name="cancellationToken">A token checked before starting and around execution.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="action"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="mode"/> is not a defined <see cref="StaExecutionMode"/>.</exception>
    public static void Run(Action action, StaExecutionMode mode, CancellationToken cancellationToken = default)
    {
        ValidateDelegate(action, nameof(action));
        ValidateExecutionMode(mode, nameof(mode));
        _ = Run(WrapAction(action), new SingleThreadedApartmentTaskOptions { ExecutionMode = mode }, new StaPlatform(), cancellationToken);
    }

    /// <summary>
    /// Synchronously executes a function in an STA apartment using the specified <see cref="StaExecutionMode"/>.
    /// </summary>
    /// <typeparam name="T">The type returned by <paramref name="func"/>.</typeparam>
    /// <param name="func">The synchronous function to execute.</param>
    /// <param name="mode">Whether to always spawn a dedicated STA thread or reuse the calling thread when already STA.</param>
    /// <param name="cancellationToken">A token checked before starting and around execution.</param>
    /// <returns>The value returned by <paramref name="func"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="func"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="mode"/> is not a defined <see cref="StaExecutionMode"/>.</exception>
    public static T Run<T>(Func<T> func, StaExecutionMode mode, CancellationToken cancellationToken = default)
    {
        ValidateDelegate(func, nameof(func));
        ValidateExecutionMode(mode, nameof(mode));
        return Run(func, new SingleThreadedApartmentTaskOptions { ExecutionMode = mode }, new StaPlatform(), cancellationToken);
    }

    /// <summary>
    /// Synchronously executes an action in an STA apartment using the specified <see cref="SingleThreadedApartmentTaskOptions"/>.
    /// </summary>
    /// <param name="action">The synchronous action to execute.</param>
    /// <param name="options">Optional execution, stack, apartment, culture/LCID, and FPU hygiene settings.</param>
    /// <param name="cancellationToken">A token checked before starting and around execution.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="action"/> is <see langword="null"/>.</exception>
    public static void Run(Action action, SingleThreadedApartmentTaskOptions? options, CancellationToken cancellationToken = default)
    {
        ValidateDelegate(action, nameof(action));
        _ = Run(WrapAction(action), options, new StaPlatform(), cancellationToken);
    }

    /// <summary>
    /// Synchronously executes an action in an STA apartment with a <see cref="StaYield"/> helper using the specified options.
    /// </summary>
    /// <param name="action">The synchronous action to execute.</param>
    /// <param name="options">Optional execution, stack, apartment, culture/LCID, and FPU hygiene settings.</param>
    /// <param name="cancellationToken">A token checked before starting and around execution.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="action"/> is <see langword="null"/>.</exception>
    public static void Run(Action<StaYield> action, SingleThreadedApartmentTaskOptions? options, CancellationToken cancellationToken = default)
    {
        ValidateDelegate(action, nameof(action));
        _ = Run(WrapYieldAction(action), options, new StaPlatform(), cancellationToken);
    }

    /// <summary>
    /// Synchronously executes a function in an STA apartment using the specified <see cref="SingleThreadedApartmentTaskOptions"/>.
    /// </summary>
    /// <typeparam name="T">The type returned by <paramref name="func"/>.</typeparam>
    /// <param name="func">The synchronous function to execute.</param>
    /// <param name="options">Optional execution, stack, apartment, culture/LCID, and FPU hygiene settings.</param>
    /// <param name="cancellationToken">A token checked before starting and around execution.</param>
    /// <returns>The value returned by <paramref name="func"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="func"/> is <see langword="null"/>.</exception>
    public static T Run<T>(Func<T> func, SingleThreadedApartmentTaskOptions? options, CancellationToken cancellationToken = default)
    {
        ValidateDelegate(func, nameof(func));
        return Run(func, options, new StaPlatform(), cancellationToken);
    }

    /// <summary>
    /// Synchronously executes a function in an STA apartment with a <see cref="StaYield"/> helper using the specified options.
    /// </summary>
    /// <typeparam name="T">The type returned by <paramref name="func"/>.</typeparam>
    /// <param name="func">The synchronous function to execute.</param>
    /// <param name="options">Optional execution, stack, apartment, culture/LCID, and FPU hygiene settings.</param>
    /// <param name="cancellationToken">A token checked before starting and around execution.</param>
    /// <returns>The value returned by <paramref name="func"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="func"/> is <see langword="null"/>.</exception>
    public static T Run<T>(Func<StaYield, T> func, SingleThreadedApartmentTaskOptions? options, CancellationToken cancellationToken = default)
    {
        ValidateDelegate(func, nameof(func));
        return Run(WrapYieldFunc(func), options, new StaPlatform(), cancellationToken);
    }

    /// <summary>
    /// Runs an action on a dedicated background STA thread.
    /// </summary>
    /// <param name="action">The synchronous action to execute on the STA thread.</param>
    /// <param name="cancellationToken">A token that can cancel the operation before or during execution.</param>
    /// <returns>A task that completes when the action and STA teardown finish, faults with the original exception, or is canceled.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="action"/> is <see langword="null"/>.</exception>
    public static Task RunAsync(Action action, CancellationToken cancellationToken = default)
    {
        ValidateDelegate(action, nameof(action));
        return RunAsync(WrapAction(action), options: null, new StaPlatform(), cancellationToken);
    }

    /// <summary>
    /// Runs an action on a dedicated background STA thread and provides a <see cref="StaYield"/> helper for cooperative message pumping.
    /// </summary>
    /// <param name="action">The synchronous action to execute on the STA thread.</param>
    /// <param name="cancellationToken">A token that can cancel the operation before or during execution.</param>
    /// <returns>A task that completes when the action and STA teardown finish, faults with the original exception, or is canceled.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="action"/> is <see langword="null"/>.</exception>
    public static Task RunAsync(Action<StaYield> action, CancellationToken cancellationToken = default)
    {
        ValidateDelegate(action, nameof(action));
        return RunAsync(WrapYieldAction(action), options: null, new StaPlatform(), cancellationToken);
    }

    /// <summary>
    /// Runs a delegate on a dedicated STA thread and provides a <see cref="StaYield"/> helper for cooperative message pumping.
    /// </summary>
    /// <typeparam name="T">The type returned by the delegate.</typeparam>
    /// <param name="func">The delegate to execute on the STA thread.</param>
    /// <param name="cancellationToken">A token that can cancel the operation before or during execution.</param>
    /// <returns>A task that completes with the delegate result, faults with the original exception, or is canceled.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="func"/> is <see langword="null"/>.</exception>
    public static Task<T> RunAsync<T>(
        Func<StaYield, T> func,
        CancellationToken cancellationToken = default)
    {
        ValidateDelegate(func, nameof(func));
        return RunAsync(WrapYieldFunc(func), options: null, new StaPlatform(), cancellationToken);
    }

    // ReSharper disable once InconsistentNaming
    // ReSharper disable once MemberCanBePrivate.Global

    /// <summary>
    /// Runs a delegate on a dedicated STA thread.
    /// </summary>
    /// <typeparam name="T">The type returned by the delegate.</typeparam>
    /// <param name="func">The delegate to execute on the STA thread.</param>
    /// <param name="cancellationToken">A token that can cancel the operation before or during execution.</param>
    /// <returns>A task that completes with the delegate result, faults with the original exception, or is canceled.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="func"/> is <see langword="null"/>.</exception>
    public static Task<T> RunAsync<T>(
        Func<T> func,
        CancellationToken cancellationToken = default)
    {
        ValidateDelegate(func, nameof(func));
        return RunAsync(func, options: null, new StaPlatform(), cancellationToken);
    }

    /// <summary>
    /// Runs an action on a dedicated background STA thread using the specified <see cref="SingleThreadedApartmentTaskOptions"/>.
    /// </summary>
    /// <param name="action">The synchronous action to execute on the STA thread.</param>
    /// <param name="options">Optional stack, apartment, culture/LCID, and FPU hygiene settings.</param>
    /// <param name="cancellationToken">A token that can cancel the operation before or during execution.</param>
    /// <returns>A task that completes when the action and STA teardown finish, faults with the original exception, or is canceled.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="action"/> is <see langword="null"/>.</exception>
    public static Task RunAsync(
        Action action,
        SingleThreadedApartmentTaskOptions? options,
        CancellationToken cancellationToken = default)
    {
        ValidateDelegate(action, nameof(action));
        return RunAsync(WrapAction(action), options, new StaPlatform(), cancellationToken);
    }

    /// <summary>
    /// Runs an action on a dedicated background STA thread with a <see cref="StaYield"/> helper using the specified options.
    /// </summary>
    /// <param name="action">The synchronous action to execute on the STA thread.</param>
    /// <param name="options">Optional stack, apartment, culture/LCID, and FPU hygiene settings.</param>
    /// <param name="cancellationToken">A token that can cancel the operation before or during execution.</param>
    /// <returns>A task that completes when the action and STA teardown finish, faults with the original exception, or is canceled.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="action"/> is <see langword="null"/>.</exception>
    public static Task RunAsync(
        Action<StaYield> action,
        SingleThreadedApartmentTaskOptions? options,
        CancellationToken cancellationToken = default)
    {
        ValidateDelegate(action, nameof(action));
        return RunAsync(WrapYieldAction(action), options, new StaPlatform(), cancellationToken);
    }

    /// <summary>
    /// Runs a delegate on a dedicated background STA thread using the specified <see cref="SingleThreadedApartmentTaskOptions"/>.
    /// </summary>
    /// <typeparam name="T">The type returned by <paramref name="func"/>.</typeparam>
    /// <param name="func">The synchronous delegate to execute on the STA thread.</param>
    /// <param name="options">Optional stack, apartment, culture/LCID, and FPU hygiene settings.</param>
    /// <param name="cancellationToken">A token that can cancel the operation before or during execution.</param>
    /// <returns>A task that completes with the delegate result, faults with the original exception, or is canceled.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="func"/> is <see langword="null"/>.</exception>
    public static Task<T> RunAsync<T>(
        Func<T> func,
        SingleThreadedApartmentTaskOptions? options,
        CancellationToken cancellationToken = default)
    {
        ValidateDelegate(func, nameof(func));
        return RunAsync(func, options, new StaPlatform(), cancellationToken);
    }

    /// <summary>
    /// Runs a delegate on a dedicated background STA thread with a <see cref="StaYield"/> helper using the specified options.
    /// </summary>
    /// <typeparam name="T">The type returned by <paramref name="func"/>.</typeparam>
    /// <param name="func">The synchronous delegate to execute on the STA thread.</param>
    /// <param name="options">Optional stack, apartment, culture/LCID, and FPU hygiene settings.</param>
    /// <param name="cancellationToken">A token that can cancel the operation before or during execution.</param>
    /// <returns>A task that completes with the delegate result, faults with the original exception, or is canceled.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="func"/> is <see langword="null"/>.</exception>
    public static Task<T> RunAsync<T>(
        Func<StaYield, T> func,
        SingleThreadedApartmentTaskOptions? options,
        CancellationToken cancellationToken = default)
    {
        ValidateDelegate(func, nameof(func));
        return RunAsync(WrapYieldFunc(func), options, new StaPlatform(), cancellationToken);
    }

    /// <summary>
    /// Runs an action on a dedicated STA thread and applies a timeout to the resulting wait.
    /// </summary>
    /// <param name="timeSpan">The maximum amount of time to wait for the action to complete.</param>
    /// <param name="action">The synchronous action to execute on the STA thread.</param>
    /// <param name="cancellationToken">A token that can cancel the wait before the timeout expires.</param>
    /// <returns>A task that completes when the action finishes, faults with the original exception, times out, or is canceled.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="action"/> is <see langword="null"/>.</exception>
    public static Task RunWithTimeoutAsync(
        TimeSpan timeSpan,
        Action action,
        CancellationToken cancellationToken = default)
    {
        TimeoutValidation.Validate(timeSpan, nameof(timeSpan));
        ValidateDelegate(action, nameof(action));
        var source = RunAsync(action, cancellationToken);
        TaskWait.ObserveFault(source);
        return source.TimeoutAfterAsync(timeSpan, cancellationToken);
    }

    /// <summary>
    /// Runs a delegate on a dedicated STA thread and applies a timeout to the resulting task.
    /// </summary>
    /// <typeparam name="T">The type returned by the delegate.</typeparam>
    /// <param name="timeSpan">The maximum amount of time to wait for the delegate to complete.</param>
    /// <param name="func">The delegate to execute on the STA thread.</param>
    /// <param name="cancellationToken">A token that can cancel the operation before the timeout expires.</param>
    /// <returns>A task that completes with the delegate result, faults with the original exception, times out, or is canceled.</returns>
    public static Task<T> RunWithTimeoutAsync<T>(
        TimeSpan timeSpan,
        Func<T> func,
        CancellationToken cancellationToken = default)
    {
        TimeoutValidation.Validate(timeSpan, nameof(timeSpan));
        var source = RunAsync(func, cancellationToken);
        TaskWait.ObserveFault(source);
        return source.TimeoutAfterAsync(timeSpan, cancellationToken);
    }

    internal static T Run<T>(
        Func<T> func,
        SingleThreadedApartmentTaskOptions? options,
        StaPlatform platform,
        CancellationToken cancellationToken)
    {
        var captured = new CapturedTaskOptions(options);
        cancellationToken.ThrowIfCancellationRequested();

        if (captured.ExecutionMode == StaExecutionMode.ReuseCurrentStaThread
            && StaApartment.IsCurrentThreadSta(platform, verifyNativeApartment: false))
        {
            return RunInlineOnCurrentStaThread(func, captured, platform, cancellationToken);
        }

        return RunOnDedicatedThreadSync(func, captured, platform, cancellationToken);
    }

    internal static Task<T> RunAsync<T>(Func<T> func, StaPlatform platform, CancellationToken cancellationToken)
    {
        return RunAsync(func, options: null, platform, cancellationToken);
    }

    internal static Task<T> RunAsync<T>(
        Func<T> func,
        SingleThreadedApartmentTaskOptions? options,
        StaPlatform platform,
        CancellationToken cancellationToken)
    {
        var captured = new CapturedTaskOptions(options);
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<T>(cancellationToken);
        }

        captured.BeforeThreadStart?.Invoke();
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = CreateDedicatedThread(() => ExecuteAsyncWorker(func, captured, platform, tcs, cancellationToken), captured);
        platform.Start(thread);
        return tcs.Task;
    }

#pragma warning disable CC0031 // Validated by the public entry point before wrapping.
    private static Func<bool> WrapAction(Action action)
    {
        return () =>
        {
            action();
            return true;
        };
    }

    private static Func<bool> WrapYieldAction(Action<StaYield> action)
    {
        return () =>
        {
            action(new StaYield());
            return true;
        };
    }

    private static Func<T> WrapYieldFunc<T>(Func<StaYield, T> func)
    {
        return () => func(new StaYield());
    }
#pragma warning restore CC0031

    private static void ValidateDelegate(Delegate work, string parameterName)
    {
#if NET8_0_OR_GREATER
#pragma warning disable S3236 // Preserve the public parameter name through this shared guard.
        ArgumentNullException.ThrowIfNull(work, parameterName);
#pragma warning restore S3236
#else
        if (work is null)
        {
            throw new ArgumentNullException(parameterName);
        }
#endif
    }

    private static void ValidateExecutionMode(StaExecutionMode mode, string parameterName)
    {
        if (mode != StaExecutionMode.DedicatedThread && mode != StaExecutionMode.ReuseCurrentStaThread)
        {
#pragma warning disable S3236 // Preserve the public parameter name through this shared guard.
            throw new ArgumentOutOfRangeException(parameterName, mode, $"Unsupported {nameof(StaExecutionMode)} value: {mode}.");
#pragma warning restore S3236
        }
    }

    private static Thread CreateDedicatedThread(ThreadStart start, CapturedTaskOptions captured)
    {
        var thread = captured.MaxStackSize > 0
            ? new Thread(start, captured.MaxStackSize)
            : new Thread(start);
        thread.IsBackground = true;
        thread.Name = captured.ThreadName;
        return thread;
    }

    private static T RunInlineOnCurrentStaThread<T>(
        Func<T> func,
        CapturedTaskOptions captured,
        StaPlatform platform,
        CancellationToken token)
    {
        captured.BeforeThreadStart?.Invoke();
        using var apartmentScope = StaApartment.EnterScope(platform, initializeOle: true, captured.VerifyNativeStaApartment);
        using var cultureScope = captured.HasCultureOrLocale
            ? ThreadCultureScope.Enter(captured.ThreadCulture, captured.ThreadUICulture, captured.RawWindowsLocaleId, platform)
            : null;
        if (captured.ResetFloatingPointMaskBeforeExit)
        {
            platform.ResetFloatingPointState();
        }

        try
        {
            token.ThrowIfCancellationRequested();
#pragma warning disable CC0031 // Validated by the public entry point.
            var result = func();
#pragma warning restore CC0031
            token.ThrowIfCancellationRequested();
            return result;
        }
        finally
        {
            if (captured.ResetFloatingPointMaskBeforeExit)
            {
                platform.ResetFloatingPointState();
            }
        }
    }

    private static T RunOnDedicatedThreadSync<T>(
        Func<T> func,
        CapturedTaskOptions captured,
        StaPlatform platform,
        CancellationToken token)
    {
        captured.BeforeThreadStart?.Invoke();
        var outcome = default((T Result, Exception? Failure));
        var thread = CreateDedicatedThread(
            () => outcome = ExecuteOnDedicatedThread(func, captured, platform, token),
            captured);
        platform.Start(thread);
        thread.Join();

        if (outcome.Failure is { } failure)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }

        return outcome.Result;
    }

    private static void ExecuteAsyncWorker<T>(
        Func<T> func,
        CapturedTaskOptions captured,
        StaPlatform platform,
        TaskCompletionSource<T> completion,
        CancellationToken token)
    {
        var (result, failure) = ExecuteOnDedicatedThread(func, captured, platform, token);
        if (failure is OperationCanceledException canceled &&
            (canceled.CancellationToken == token || token.IsCancellationRequested))
        {
            completion.TrySetCanceled(token);
        }
        else if (failure is not null)
        {
            completion.TrySetException(failure);
        }
        else
        {
            completion.TrySetResult(result);
        }
    }

    private static (T Result, Exception? Failure) ExecuteOnDedicatedThread<T>(
        Func<T> func,
        CapturedTaskOptions captured,
        StaPlatform platform,
        CancellationToken token)
    {
        var oleInitialized = false;
        var comInitialized = false;
        var result = default(T)!;
        Exception? failure = null;
        try
        {
            token.ThrowIfCancellationRequested();
            platform.Initialize();
            oleInitialized = true;
            if (captured.VerifyNativeStaApartment)
            {
                platform.EnsureNativeStaApartment(ref comInitialized);
            }

            if (captured.ResetFloatingPointMaskBeforeExit)
            {
                platform.ResetFloatingPointState();
            }

            ApplyDedicatedThreadCultureAndLocale(captured, platform);
            token.ThrowIfCancellationRequested();
#pragma warning disable CC0031 // Validated by the public entry point.
            result = func();
#pragma warning restore CC0031
            token.ThrowIfCancellationRequested();
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        finally
        {
            if (captured.ResetFloatingPointMaskBeforeExit)
            {
                failure = StaPlatform.InvokeCleanupStep(platform.ResetFloatingPointState, failure);
            }

            failure = Cleanup(platform, comInitialized, oleInitialized, failure);
        }

        return (result, failure);
    }

    private static void ApplyDedicatedThreadCultureAndLocale(CapturedTaskOptions captured, StaPlatform platform)
    {
        if (captured.ThreadCulture is not null && !ReferenceEquals(CultureInfo.CurrentCulture, captured.ThreadCulture))
        {
            CultureInfo.CurrentCulture = captured.ThreadCulture;
        }

        if (captured.ThreadUICulture is not null && !ReferenceEquals(CultureInfo.CurrentUICulture, captured.ThreadUICulture))
        {
            CultureInfo.CurrentUICulture = captured.ThreadUICulture;
        }

        if (captured.WindowsThreadLocaleId.HasValue)
        {
            platform.SetThreadLocale(captured.WindowsThreadLocaleId.Value);
        }
    }

    private static Exception? Cleanup(
        StaPlatform platform,
        bool comInitialized,
        bool oleInitialized,
        Exception? primary)
    {
        if (oleInitialized)
        {
            primary = StaPlatform.InvokeCleanupStep(() => platform.Pump(preserveQuit: false), primary);
        }

        if (comInitialized)
        {
            primary = StaPlatform.InvokeCleanupStep(platform.UninitializeCom, primary);
        }

        if (oleInitialized)
        {
            primary = StaPlatform.InvokeCleanupStep(platform.Uninitialize, primary);
        }

        return primary;
    }

    private readonly struct CapturedTaskOptions
    {
        public CapturedTaskOptions(SingleThreadedApartmentTaskOptions? options)
        {
            var threadName = options?.ThreadName ?? string.Empty;
            ThreadName = string.IsNullOrWhiteSpace(threadName)
                ? SingleThreadedApartmentTaskOptions.DefaultThreadName
                : threadName;
            MaxStackSize = options?.MaxStackSize ?? 0;
            ExecutionMode = options?.ExecutionMode ?? StaExecutionMode.DedicatedThread;
            BeforeThreadStart = options?.BeforeThreadStart;
            VerifyNativeStaApartment = options?.VerifyNativeStaApartment ?? true;
            var culture = options?.ThreadCulture;
            ThreadCulture = culture is null ? null : CultureInfo.ReadOnly(culture);
            var uiCulture = options?.ThreadUICulture;
            ThreadUICulture = uiCulture is null ? null : CultureInfo.ReadOnly(uiCulture);
            RawWindowsLocaleId = options?.WindowsThreadLocaleId;
            ResetFloatingPointMaskBeforeExit = options?.ResetFloatingPointMaskBeforeExit ?? false;

            if (MaxStackSize < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(options), "MaxStackSize must be non-negative.");
            }

            ValidateExecutionMode(ExecutionMode, nameof(options));
            if (RawWindowsLocaleId is <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(options), "WindowsThreadLocaleId must be positive or null.");
            }

            WindowsThreadLocaleId = (uint?)RawWindowsLocaleId;
        }

        public string ThreadName { get; }

        public int MaxStackSize { get; }

        public StaExecutionMode ExecutionMode { get; }

        public Action? BeforeThreadStart { get; }

        public bool VerifyNativeStaApartment { get; }

        public CultureInfo? ThreadCulture { get; }

        public CultureInfo? ThreadUICulture { get; }

        public int? RawWindowsLocaleId { get; }

        public uint? WindowsThreadLocaleId { get; }

        public bool ResetFloatingPointMaskBeforeExit { get; }

        public bool HasCultureOrLocale =>
            ThreadCulture is not null || ThreadUICulture is not null || RawWindowsLocaleId.HasValue;
    }
}
