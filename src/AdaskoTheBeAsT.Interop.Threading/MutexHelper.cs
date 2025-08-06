using System;
using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;

namespace AdaskoTheBeAsT.Interop.Threading;

/// <summary>
/// http://stackoverflow.com/questions/229565/what-is-a-good-pattern-for-using-a-global-mutex-in-c.
/// </summary>
public static class MutexHelper
{
    public static T RunInMutex<T>(string name, Func<T> func)
    {
        return RunInMutex(name, Timeout.InfiniteTimeSpan, isGlobal: true, func);
    }

    public static T RunInMutex<T>(string name, TimeSpan timeout, Func<T> func)
    {
        return RunInMutex(name, timeout, isGlobal: true, func);
    }

#pragma warning disable MA0051
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
