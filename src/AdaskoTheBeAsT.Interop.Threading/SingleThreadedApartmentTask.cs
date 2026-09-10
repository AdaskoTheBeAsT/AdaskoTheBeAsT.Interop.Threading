using System;
#if NET8_0_OR_GREATER
using System.Runtime.Versioning;
#endif
using System.Threading;
using System.Threading.Tasks;

namespace AdaskoTheBeAsT.Interop.Threading;

/// <summary>
/// Runs delegates on a dedicated background STA thread.
/// Each call owns one OLE-initialized thread. Task completion includes a bounded final pump and OLE cleanup.
/// </summary>
/// <remarks>Delegates must be synchronous. Neither async lambdas nor Unwrap preserve STA affinity across awaits.</remarks>
#if NET8_0_OR_GREATER
[SupportedOSPlatform("windows")]
#endif
public static class SingleThreadedApartmentTask
{
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
        CancellationToken cancellationToken)
    {
#if NET6_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(func);
#else
        if (func == null)
        {
            throw new ArgumentNullException(nameof(func));
        }
#endif

        return RunAsync(() => func(new StaYield()), cancellationToken);
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
        CancellationToken cancellationToken)
    {
#if NET6_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(func);
#else
        if (func == null)
        {
            throw new ArgumentNullException(nameof(func));
        }
#endif

        return RunAsync(func, cancellationToken, new StaPlatform());
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
        CancellationToken cancellationToken)
    {
        TimeoutValidation.Validate(timeSpan, nameof(timeSpan));
        var source = RunAsync(func, cancellationToken);
        TaskWait.ObserveFault(source);
        return source.TimeoutAfterAsync(timeSpan, cancellationToken);
    }

    internal static Task<T> RunAsync<T>(Func<T> func, CancellationToken cancellationToken, StaPlatform platform)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<T>(cancellationToken);
        }

        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => Execute(func, cancellationToken, platform, tcs))
        {
            IsBackground = true,
            Name = "STA Task Thread",
        };
        platform.Start(thread);
        return tcs.Task;
    }

    private static void Execute<T>(
        Func<T> func, CancellationToken token, StaPlatform platform, TaskCompletionSource<T> completion)
    {
        var initialized = false;
        var result = default(T)!;
        Exception? failure = null;
        try
        {
            token.ThrowIfCancellationRequested();
            platform.Initialize();
            initialized = true;
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
            if (initialized)
            {
                failure = Cleanup(platform, failure);
            }
        }

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

    private static Exception? Cleanup(StaPlatform platform, Exception? primary)
    {
        try
        {
            platform.Pump(preserveQuit: false);
        }
        catch (Exception ex)
        {
            primary ??= ex;
        }
        finally
        {
            try
            {
                platform.Uninitialize();
            }
            catch (Exception ex)
            {
                primary ??= ex;
            }
        }

        return primary;
    }
}
