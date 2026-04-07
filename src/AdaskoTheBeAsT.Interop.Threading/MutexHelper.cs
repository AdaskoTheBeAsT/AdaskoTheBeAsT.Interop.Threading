using System;
using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;

namespace AdaskoTheBeAsT.Interop.Threading;

/// <summary>
/// Executes delegates while holding a named operating-system mutex.
/// This is useful when work must be serialized across threads or processes.
/// </summary>
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
        var allowEveryoneRule = new MutexAccessRule(
            new SecurityIdentifier(WellKnownSidType.WorldSid, domainSid: null),
            MutexRights.FullControl,
            AccessControlType.Allow);

        var securitySettings = new MutexSecurity();
        securitySettings.AddAccessRule(allowEveryoneRule);

        using (var mutex = new Mutex(
                   initiallyOwned: false,
                   mutexName,
                   out bool createdNew))
        {
            if (createdNew)
            {
                try
                {
                    mutex.SetAccessControl(securitySettings);
                }
#pragma warning disable CC0004
                catch (PlatformNotSupportedException)
                {
                    // you're on a runtime that doesn't support it—just continue
                }
#pragma warning restore CC0004
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

                    // Log the fact the mutex was abandoned in another process, it will still get acquired
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
}
