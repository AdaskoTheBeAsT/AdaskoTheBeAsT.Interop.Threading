using System;
using System.Diagnostics;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
#if NET8_0_OR_GREATER
using System.Runtime.Versioning;
#endif

namespace AdaskoTheBeAsT.Interop.Threading;

/// <summary>
/// Executes delegates while holding a named operating-system mutex.
/// This is useful when work must be serialized across threads or processes.
/// </summary>
#if NET8_0_OR_GREATER
[SupportedOSPlatform("windows")]
#endif
public static class MutexHelper
{
    // Computed once per process. The ACL (Everyone: FullControl) is invariant, so the
    // MutexSecurity object is safe to reuse across all calls and threads.
    private static readonly MutexSecurity _securitySettings = BuildEveryoneAllowSecurity();

    // On some runtimes (notably .NET 10+ restoring cross-platform semantics for
    // Mutex) the extension method System.Threading.Mutex.SetAccessControl is no
    // longer present as an instance member. We look it up reflectively once and
    // invoke it when available; otherwise we skip applying ACLs and emit trace
    // diagnostics when unsupported, while preserving the historical
    // PlatformNotSupportedException-swallowing behavior.
    private static readonly MethodInfo? _setAccessControl = ResolveSetAccessControl();

    /// <summary>
    /// Runs a delegate while holding a named global mutex and waits indefinitely to acquire it.
    /// </summary>
    /// <typeparam name="T">The type returned by the delegate.</typeparam>
    /// <param name="name">The mutex name, without the <c>Global\</c> prefix.</param>
    /// <param name="func">The delegate to execute after the mutex is acquired.</param>
    /// <returns>The value returned by <paramref name="func"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="func"/> is <see langword="null"/>.</exception>
    public static T RunInMutex<T>(string name, Func<T> func)
    {
        return RunInMutex(name, Timeout.InfiniteTimeSpan, isGlobal: true, func);
    }

    /// <summary>
    /// Runs a delegate while holding a named global mutex.
    /// </summary>
    /// <typeparam name="T">The type returned by the delegate.</typeparam>
    /// <param name="name">The mutex name, without the <c>Global\</c> prefix.</param>
    /// <param name="timeout">How long to wait to acquire the mutex.</param>
    /// <param name="func">The delegate to execute after the mutex is acquired.</param>
    /// <returns>The value returned by <paramref name="func"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="func"/> is <see langword="null"/>.</exception>
    /// <exception cref="TimeoutException">Thrown when the mutex cannot be acquired within <paramref name="timeout"/>.</exception>
    public static T RunInMutex<T>(string name, TimeSpan timeout, Func<T> func)
    {
        return RunInMutex(name, timeout, isGlobal: true, func);
    }

    // MA0051: method length is acceptable here — the acquire/execute/release flow
    // is tightly coupled and splitting it would make the locking lifecycle harder
    // to reason about.
#pragma warning disable MA0051
    /// <summary>
    /// Runs a delegate while holding a named mutex, optionally using the machine-wide <c>Global\</c> namespace.
    /// </summary>
    /// <typeparam name="T">The type returned by the delegate.</typeparam>
    /// <param name="name">The mutex name.</param>
    /// <param name="timeout">How long to wait to acquire the mutex.</param>
    /// <param name="isGlobal"><see langword="true"/> to use a machine-wide mutex name prefixed with <c>Global\</c>; otherwise use a local name.</param>
    /// <param name="func">The delegate to execute after the mutex is acquired.</param>
    /// <returns>The value returned by <paramref name="func"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="func"/> is <see langword="null"/>.</exception>
    /// <exception cref="TimeoutException">Thrown when the mutex cannot be acquired within <paramref name="timeout"/>.</exception>
    public static T RunInMutex<T>(string name, TimeSpan timeout, bool isGlobal, Func<T> func)
#pragma warning restore MA0051
    {
        if (func == null)
        {
            throw new ArgumentNullException(nameof(func));
        }

        var mutexName = isGlobal ? $"Global\\{name}" : name;

        using (var mutex = new Mutex(
                   initiallyOwned: false,
                   mutexName,
                   out bool createdNew))
        {
            if (createdNew)
            {
                TryApplyEveryoneAcl(mutex);
            }

            var hasHandle = false;
            try
            {
                try
                {
                    hasHandle = mutex.WaitOne(timeout, exitContext: false);
                    if (!hasHandle)
                    {
                        throw new TimeoutException(
                            $"Timeout waiting for exclusive access {mutexName} after {timeout}");
                    }
                }
                catch (AbandonedMutexException ex)
                {
                    Trace.TraceWarning($"Mutex '{mutexName}' was abandoned: {ex}");

                    // Log the fact the mutex was abandoned in another process, it will still get acquired.
                    hasHandle = true;
                }

                return func();
            }
            finally
            {
                if (hasHandle)
                {
                    mutex.ReleaseMutex();
                }
            }
        }
    }

    private static MutexSecurity BuildEveryoneAllowSecurity()
    {
        var allowEveryoneRule = new MutexAccessRule(
            new SecurityIdentifier(WellKnownSidType.WorldSid, domainSid: null),
            MutexRights.FullControl,
            AccessControlType.Allow);

        var securitySettings = new MutexSecurity();
        securitySettings.AddAccessRule(allowEveryoneRule);
        return securitySettings;
    }

    private static MethodInfo? ResolveSetAccessControl()
    {
        // Prefer the instance method if the current runtime has one (net462/.NET
        // Framework and some .NET target packs). When it is not present we fall
        // back to the extension method defined in the MutexAclExtensions type
        // shipped by System.Threading.AccessControl on .NET 8+.
        var instanceMethod = typeof(Mutex).GetMethod(
            "SetAccessControl",
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            types: [typeof(MutexSecurity)],
            modifiers: null);

        if (instanceMethod is not null)
        {
            return instanceMethod;
        }

        var aclExtensionsType = Type.GetType(
            "System.Threading.MutexAclExtensions, System.Threading.AccessControl",
            throwOnError: false);

        return aclExtensionsType?.GetMethod(
            "SetAccessControl",
            BindingFlags.Static | BindingFlags.Public,
            binder: null,
            types: [typeof(Mutex), typeof(MutexSecurity)],
            modifiers: null);
    }

    private static void TryApplyEveryoneAcl(Mutex mutex)
    {
        if (_setAccessControl is null)
        {
            return;
        }

        try
        {
            if (_setAccessControl.IsStatic)
            {
                _setAccessControl.Invoke(obj: null, [mutex, _securitySettings]);
            }
            else
            {
                _setAccessControl.Invoke(mutex, [_securitySettings]);
            }
        }
#pragma warning disable CA1031
        catch (TargetInvocationException ex) when (ex.InnerException is PlatformNotSupportedException)
        {
            // runtime does not support SetAccessControl — continue without ACL.
            Trace.TraceInformation($"Mutex ACL not supported on this runtime: {ex.InnerException.Message}");
        }
        catch (PlatformNotSupportedException ex)
        {
            // runtime does not support SetAccessControl — continue without ACL.
            Trace.TraceInformation($"Mutex ACL not supported on this runtime: {ex.Message}");
        }
        catch (Exception ex)
        {
            // Any other failure to apply the ACL should not prevent the caller from using the mutex.
            Trace.TraceWarning($"Failed to apply Everyone ACL to mutex: {ex}");
        }
#pragma warning restore CA1031
    }
}
