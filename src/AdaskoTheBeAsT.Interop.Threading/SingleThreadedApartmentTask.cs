using System;
using System.Threading;
using System.Threading.Tasks;

namespace AdaskoTheBeAsT.Interop.Threading;

/// <summary>
/// http://stackoverflow.com/questions/16720496/set-apartmentstate-on-a-task.
/// </summary>
public static class SingleThreadedApartmentTask
{
    // ReSharper disable once InconsistentNaming
    // ReSharper disable once MemberCanBePrivate.Global
    public static Task<T> RunAsync<T>(Func<T> func)
    {
        if (func == null)
        {
            throw new ArgumentNullException(nameof(func));
        }

        var tcs = new TaskCompletionSource<T>();
        var thread = new Thread(() =>
        {
            try
            {
                tcs.SetResult(func());
            }
            catch (Exception e)
            {
                tcs.SetException(e);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }

    public static Task<T> RunWithTimeoutAsync<T>(TimeSpan timeSpan, Func<T> func)
    {
        return RunAsync(func).TimeoutAfterAsync(timeSpan);
    }
}
