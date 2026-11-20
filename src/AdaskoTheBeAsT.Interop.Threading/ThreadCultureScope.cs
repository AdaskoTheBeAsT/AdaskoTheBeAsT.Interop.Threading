using System;
using System.Globalization;
#if NET8_0_OR_GREATER
using System.Runtime.Versioning;
#endif
using System.Threading;

namespace AdaskoTheBeAsT.Interop.Threading;

/// <summary>
/// Pins <see cref="CultureInfo.CurrentCulture"/>, <see cref="CultureInfo.CurrentUICulture"/>, and (optionally on Windows)
/// the Win32 kernel thread locale (<c>SetThreadLocale</c>) on the calling thread, restoring the previous values on disposal.
/// </summary>
public sealed class ThreadCultureScope : IDisposable
{
    private readonly CultureInfo _previousCulture;
    private readonly CultureInfo _previousUICulture;
    private readonly Action? _restoreWindowsLocale;
    private int _disposed;

    private ThreadCultureScope(
        ThreadAffinity affinity,
        CultureInfo previousCulture,
        CultureInfo previousUICulture,
        Action? restoreWindowsLocale)
    {
        Affinity = affinity;
        _previousCulture = previousCulture;
        _previousUICulture = previousUICulture;
        _restoreWindowsLocale = restoreWindowsLocale;
    }

    /// <summary>
    /// Gets the thread-affinity guard captured when the scope was entered.
    /// </summary>
    public ThreadAffinity Affinity { get; }

    /// <summary>
    /// Enters a portable culture scope on the calling thread, applying any non-<see langword="null"/>
    /// <paramref name="culture"/> and <paramref name="uiCulture"/> and restoring the prior thread cultures on <see cref="Dispose"/>.
    /// </summary>
    /// <param name="culture">The <see cref="CultureInfo.CurrentCulture"/> to set, or <see langword="null"/> to leave unchanged until disposal.</param>
    /// <param name="uiCulture">The <see cref="CultureInfo.CurrentUICulture"/> to set, or <see langword="null"/> to leave unchanged until disposal.</param>
    /// <returns>An idempotent <see cref="ThreadCultureScope"/> bound to the calling thread.</returns>
    public static ThreadCultureScope Enter(CultureInfo? culture, CultureInfo? uiCulture = null)
    {
        var affinity = ThreadAffinity.Capture();
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUICulture = CultureInfo.CurrentUICulture;
        ApplyManagedCultures(culture, uiCulture, previousCulture, previousUICulture);
        return new ThreadCultureScope(affinity, previousCulture, previousUICulture, restoreWindowsLocale: null);
    }

    /// <summary>
    /// Enters a Windows culture and Win32 kernel thread locale scope on the calling thread, applying any non-<see langword="null"/>
    /// <paramref name="culture"/>, <paramref name="uiCulture"/>, and <paramref name="windowsThreadLocaleId"/> (<c>SetThreadLocale</c>)
    /// and restoring all prior values on <see cref="Dispose"/>.
    /// </summary>
    /// <param name="culture">The <see cref="CultureInfo.CurrentCulture"/> to set, or <see langword="null"/> to leave unchanged until disposal.</param>
    /// <param name="uiCulture">The <see cref="CultureInfo.CurrentUICulture"/> to set, or <see langword="null"/> to leave unchanged until disposal.</param>
    /// <param name="windowsThreadLocaleId">
    /// Positive Win32 LCID (for example <c>0x0409</c> for <c>en-US</c> or <c>0x0809</c> for <c>en-GB</c>) to apply via <c>SetThreadLocale</c>,
    /// or <see langword="null"/> to manage managed cultures only.
    /// </param>
    /// <returns>An idempotent <see cref="ThreadCultureScope"/> bound to the calling thread.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="windowsThreadLocaleId"/> is zero or negative.</exception>
#if NET8_0_OR_GREATER
    [SupportedOSPlatform("windows")]
#endif
    public static ThreadCultureScope Enter(CultureInfo? culture, CultureInfo? uiCulture, int? windowsThreadLocaleId)
    {
        return Enter(culture, uiCulture, windowsThreadLocaleId, new StaPlatform());
    }

    /// <summary>
    /// Restores the saved <see cref="CultureInfo.CurrentCulture"/>, <see cref="CultureInfo.CurrentUICulture"/>, and
    /// (if captured) Win32 thread locale on the owning thread. Multiple calls on the owning thread are no-ops.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when called from a thread other than <see cref="Affinity"/>.</exception>
    public void Dispose()
    {
        Affinity.ThrowIfWrongThread(nameof(ThreadCultureScope));
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        RestoreManagedCultures(_previousCulture, _previousUICulture);
        _restoreWindowsLocale?.Invoke();
    }

#if NET8_0_OR_GREATER
    [SupportedOSPlatform("windows")]
#endif
    internal static ThreadCultureScope Enter(
        CultureInfo? culture,
        CultureInfo? uiCulture,
        int? windowsThreadLocaleId,
        StaPlatform platform)
    {
        if (windowsThreadLocaleId is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(windowsThreadLocaleId),
                windowsThreadLocaleId,
                "WindowsThreadLocaleId must be positive or null.");
        }

        if (!windowsThreadLocaleId.HasValue)
        {
            return Enter(culture, uiCulture);
        }

        var affinity = ThreadAffinity.Capture();
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUICulture = CultureInfo.CurrentUICulture;
        var previousLcid = platform.GetThreadLocale();

        try
        {
            ApplyManagedCultures(culture, uiCulture, previousCulture, previousUICulture);
            platform.SetThreadLocale((uint)windowsThreadLocaleId.Value);
        }
        catch
        {
            RestoreManagedCultures(previousCulture, previousUICulture);
            throw;
        }

        return new ThreadCultureScope(
            affinity,
            previousCulture,
            previousUICulture,
            CreateWindowsLocaleRestorer(platform, previousLcid));
    }

    private static void ApplyManagedCultures(
        CultureInfo? culture,
        CultureInfo? uiCulture,
        CultureInfo previousCulture,
        CultureInfo previousUICulture)
    {
        if (culture is not null && !ReferenceEquals(previousCulture, culture))
        {
            CultureInfo.CurrentCulture = culture;
        }

        if (uiCulture is not null && !ReferenceEquals(previousUICulture, uiCulture))
        {
            CultureInfo.CurrentUICulture = uiCulture;
        }
    }

    private static void RestoreManagedCultures(CultureInfo previousCulture, CultureInfo previousUICulture)
    {
        if (!ReferenceEquals(CultureInfo.CurrentCulture, previousCulture))
        {
            CultureInfo.CurrentCulture = previousCulture;
        }

        if (!ReferenceEquals(CultureInfo.CurrentUICulture, previousUICulture))
        {
            CultureInfo.CurrentUICulture = previousUICulture;
        }
    }

#if NET8_0_OR_GREATER
    [SupportedOSPlatform("windows")]
#endif
    private static Action? CreateWindowsLocaleRestorer(StaPlatform platform, uint previousLcid)
    {
        if (previousLcid == 0)
        {
            return null;
        }

        return () =>
        {
            if (platform.GetThreadLocale() != previousLcid)
            {
                platform.SetThreadLocale(previousLcid);
            }
        };
    }
}
