using System;
using System.Diagnostics;
#if NET8_0_OR_GREATER
using System.Runtime.Versioning;
#endif
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;

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
#if NET6_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(func);
#else
        if (func == null)
        {
            throw new ArgumentNullException(nameof(func));
        }
#endif

        ValidateName(name, allowPrefix: !isGlobal);
        TimeoutValidation.Validate(timeout, nameof(timeout));
        var mutexName = isGlobal ? $"Global\\{name}" : name;
        ValidateName(mutexName, allowPrefix: true);

        using (var mutex = CreateMutex(mutexName))
        {
            var hasHandle = false;
            try
            {
                try
                {
                    hasHandle = mutex.WaitOne(timeout, exitContext: false);
                    if (!hasHandle)
                    {
                        throw new TimeoutException(
                            $"Timeout waiting for exclusive access after {timeout}");
                    }
                }
                catch (AbandonedMutexException ex)
                {
                    Trace.TraceWarning($"A named mutex was abandoned ({ex.GetType().Name}); protected state may require recovery.");

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

    /// <summary>
    /// Executes synchronous code with explicit creation, abandonment, and cancelable-acquisition policies.
    /// Cancellation wins before delegate invocation; any acquired mutex is always released on the acquiring thread.
    /// </summary>
    /// <typeparam name="T">Result type.</typeparam>
    /// <param name="name">Unqualified name. Select the namespace through options, not a prefix.</param>
    /// <param name="options">Security and acquisition policies.</param>
    /// <param name="func">Synchronous delegate, never an async lambda.</param>
    /// <param name="cancellationToken">Cancels acquisition, not the executing delegate.</param>
    /// <returns>The delegate result.</returns>
    public static T RunInMutex<T>(
        string name, MutexExecutionOptions options, Func<T> func, CancellationToken cancellationToken = default)
    {
#if NET8_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(func);
#else
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        if (func == null)
        {
            throw new ArgumentNullException(nameof(func));
        }
#endif
        ValidateName(name, allowPrefix: false);
        TimeoutValidation.Validate(options.Timeout, nameof(options));
        cancellationToken.ThrowIfCancellationRequested();
        var mutexName = options.IsGlobal ? $"Global\\{name}" : name;
        ValidateName(mutexName, allowPrefix: true);
        using var mutex = OpenOrCreateMutex(mutexName, options.Security ?? BuildCurrentUserSecurity());
        var acquired = false;
        try
        {
            try
            {
                acquired = Acquire(mutex, options.Timeout, cancellationToken);
            }
            catch (AbandonedMutexException)
            {
                acquired = true;
                if (options.FailOnAbandonedMutex)
                {
                    throw;
                }

                Trace.TraceWarning("A named mutex was abandoned; protected state may require recovery.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (!acquired)
            {
                throw new TimeoutException("Timeout waiting for exclusive mutex access.");
            }

            return func();
        }
        finally
        {
            if (acquired)
            {
                mutex.ReleaseMutex();
            }
        }
    }

    private static bool Acquire(Mutex mutex, TimeSpan timeout, CancellationToken token)
    {
        return token.CanBeCanceled
            ? WaitHandle.WaitAny([mutex, token.WaitHandle], timeout) == 0
            : mutex.WaitOne(timeout);
    }

    private static void ValidateName(string name, bool allowPrefix)
    {
#if NET8_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(name);
#else
        if (name is null)
        {
            throw new ArgumentNullException(nameof(name));
        }
#endif
        var unqualified = name;
        if (allowPrefix && name.StartsWith("Global\\", StringComparison.Ordinal))
        {
            unqualified = name.Substring("Global\\".Length);
        }
        else if (allowPrefix && name.StartsWith("Local\\", StringComparison.Ordinal))
        {
            unqualified = name.Substring("Local\\".Length);
        }

#if NET8_0_OR_GREATER
        if (unqualified.Length == 0 || unqualified.Contains('\\', StringComparison.Ordinal) || name.Contains('\0', StringComparison.Ordinal) || name.Length > 260)
#else
        if (unqualified.Length == 0 || unqualified.Contains("\\") || name.Contains("\0") || name.Length > 260)
#endif
        {
            throw new ArgumentException("Use a non-empty mutex name without embedded namespace separators or null characters.", nameof(name));
        }
    }

    private static MutexSecurity BuildCurrentUserSecurity()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var user = identity.User ?? throw new InvalidOperationException("The current Windows user has no SID.");
        var security = new MutexSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new MutexAccessRule(user, MutexRights.Synchronize | MutexRights.Modify, AccessControlType.Allow));
        return security;
    }

    private static Mutex OpenOrCreateMutex(string name, MutexSecurity security)
    {
        try
        {
            return OpenForSynchronization(name);
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            try
            {
                return MutexAcl.Create(initiallyOwned: false, name, out _, security);
            }
            catch (UnauthorizedAccessException)
            {
                // A competing creator may have won with a restricted ACL.
                return OpenForSynchronization(name);
            }
        }
    }

    private static Mutex OpenForSynchronization(string name)
    {
        return MutexAcl.OpenExisting(name, MutexRights.Synchronize | MutexRights.Modify);
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

    private static Mutex CreateMutex(string name)
    {
        // Apply a fresh security descriptor atomically when creating the mutex.
        // Opening an existing mutex leaves its security descriptor unchanged.
        return MutexAcl.Create(initiallyOwned: false, name, out _, BuildEveryoneAllowSecurity());
    }
}
