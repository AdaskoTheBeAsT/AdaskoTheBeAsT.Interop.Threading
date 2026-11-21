using System;
using System.Globalization;
using System.Runtime.InteropServices;
#if NET8_0_OR_GREATER
using System.Runtime.Versioning;
#endif
using System.Threading;

namespace AdaskoTheBeAsT.Interop.Threading;

/// <summary>
/// Inspects and initializes Windows COM/OLE single-threaded apartment (STA) state on the current thread.
/// </summary>
#if NET8_0_OR_GREATER
[SupportedOSPlatform("windows")]
#endif
public static class StaApartment
{
    /// <summary>
    /// Determines whether the calling thread is configured as a managed STA thread and, optionally,
    /// initialized in a native COM STA apartment.
    /// </summary>
    /// <param name="verifyNativeApartment">
    /// <see langword="true"/> (default) to also verify native COM apartment membership via <c>CoGetApartmentType</c>;
    /// <see langword="false"/> to check only <see cref="Thread.GetApartmentState"/>.
    /// </param>
    /// <returns><see langword="true"/> when the current thread satisfies the requested STA checks; otherwise <see langword="false"/>.</returns>
    public static bool IsCurrentThreadSta(bool verifyNativeApartment = true)
    {
        return IsCurrentThreadSta(new StaPlatform(), verifyNativeApartment);
    }

    /// <summary>
    /// Verifies that the calling thread is configured as a managed STA thread and, optionally,
    /// initialized in a native COM STA apartment, throwing <see cref="InvalidOperationException"/> otherwise.
    /// </summary>
    /// <param name="verifyNativeApartment">
    /// <see langword="true"/> (default) to also verify native COM apartment membership via <c>CoGetApartmentType</c>;
    /// <see langword="false"/> to check only <see cref="Thread.GetApartmentState"/>.
    /// </param>
    /// <exception cref="InvalidOperationException">Thrown when the current thread is not in the required STA state.</exception>
    public static void VerifyCurrentThreadSta(bool verifyNativeApartment = true)
    {
        VerifyCurrentThreadSta(new StaPlatform(), verifyNativeApartment);
    }

    /// <summary>
    /// Enters a balanced COM STA apartment scope (<c>CoInitializeEx(COINIT_APARTMENTTHREADED)</c>) and optional
    /// <c>OleInitialize</c> lease on the current STA thread, registering <c>OleMainThreadWndClass</c> when needed
    /// and verifying native STA membership.
    /// </summary>
    /// <param name="initializeOle">
    /// <see langword="true"/> to also invoke <c>OleInitialize</c> and balance it with <c>OleUninitialize</c> on disposal;
    /// <see langword="false"/> (default) to manage <c>CoInitializeEx(COINIT_APARTMENTTHREADED)</c> / <c>CoUninitialize</c> only.
    /// </param>
    /// <returns>An idempotent <see cref="StaApartmentScope"/> bound to the calling thread.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the calling thread is configured or initialized as MTA rather than STA.
    /// </exception>
    /// <exception cref="COMException">
    /// Thrown when <c>CoInitializeEx</c>, <c>OleInitialize</c>, or <c>CoGetApartmentType</c> fails with a non-apartment HRESULT.
    /// </exception>
    public static StaApartmentScope EnterScope(bool initializeOle = false)
    {
        return EnterScope(new StaPlatform(), initializeOle, verifyNativeApartment: true);
    }

    internal static bool IsCurrentThreadSta(StaPlatform platform, bool verifyNativeApartment)
    {
        if (platform.GetManagedApartmentState() != ApartmentState.STA)
        {
            return false;
        }

        if (!verifyNativeApartment)
        {
            return true;
        }

        var hr = platform.GetApartmentType(out var apartmentType, out var apartmentQualifier);
        return hr >= 0 && StaPlatform.IsNativeStaApartment(apartmentType, apartmentQualifier);
    }

    internal static void VerifyCurrentThreadSta(StaPlatform platform, bool verifyNativeApartment)
    {
        var managedState = platform.GetManagedApartmentState();
        if (managedState != ApartmentState.STA)
        {
            throw new InvalidOperationException(
                $"Current thread {Environment.CurrentManagedThreadId.ToString(CultureInfo.InvariantCulture)} has managed apartment state {managedState} instead of STA.");
        }

        if (!verifyNativeApartment)
        {
            return;
        }

        var hr = platform.GetApartmentType(out var apartmentType, out var apartmentQualifier);
        if (hr == NativeMethods.CO_E_NOTINITIALIZED)
        {
            throw new InvalidOperationException(
                $"Native COM apartment is not initialized on managed STA thread {Environment.CurrentManagedThreadId.ToString(CultureInfo.InvariantCulture)} (CO_E_NOTINITIALIZED). Call {nameof(StaApartment)}.{nameof(EnterScope)}() first.",
                new COMException("COM is not initialized on the current thread.", hr));
        }

        if (hr < 0)
        {
            throw new InvalidOperationException(
                $"CoGetApartmentType failed on thread {Environment.CurrentManagedThreadId.ToString(CultureInfo.InvariantCulture)} (HRESULT: 0x{hr.ToString("X8", CultureInfo.InvariantCulture)}).",
                new COMException($"CoGetApartmentType failed (HRESULT: 0x{hr.ToString("X8", CultureInfo.InvariantCulture)}).", hr));
        }

        if (!StaPlatform.IsNativeStaApartment(apartmentType, apartmentQualifier))
        {
            throw new InvalidOperationException(
                $"Current thread {Environment.CurrentManagedThreadId.ToString(CultureInfo.InvariantCulture)} is in native COM apartment {apartmentType.ToString(CultureInfo.InvariantCulture)} (qualifier: {apartmentQualifier.ToString(CultureInfo.InvariantCulture)}) instead of STA.");
        }
    }

    internal static StaApartmentScope EnterScope(
        StaPlatform platform,
        bool initializeOle,
        bool verifyNativeApartment = true)
    {
        EnsureManagedStaThread(platform);
        var comHr = platform.InitializeComSta();
        if (comHr == NativeMethods.RPC_E_CHANGED_MODE)
        {
            var inner = new COMException(
                $"CoInitializeEx(COINIT_APARTMENTTHREADED) failed (HRESULT: 0x{comHr.ToString("X8", CultureInfo.InvariantCulture)}).",
                comHr);
            throw new InvalidOperationException(
                $"Cannot enter an STA apartment scope on thread {Environment.CurrentManagedThreadId.ToString(CultureInfo.InvariantCulture)} because COM is already initialized as MTA (RPC_E_CHANGED_MODE).",
                inner);
        }

        if (comHr < 0)
        {
            throw new COMException(
                $"CoInitializeEx(COINIT_APARTMENTTHREADED) failed (HRESULT: 0x{comHr.ToString("X8", CultureInfo.InvariantCulture)}).",
                comHr);
        }

        const bool comInitialized = true;
        var oleInitialized = false;
        try
        {
            if (initializeOle)
            {
                platform.Initialize();
                oleInitialized = true;
            }

            if (verifyNativeApartment)
            {
                VerifyNativeStaAfterComInit(platform);
            }

            return new StaApartmentScope(platform, ThreadAffinity.Capture(), comInitialized, oleInitialized);
        }
        catch
        {
            RollbackInitialization(platform, comInitialized, oleInitialized);
            throw;
        }
    }

    private static void EnsureManagedStaThread(StaPlatform platform)
    {
        var managedState = platform.GetManagedApartmentState();
        if (managedState == ApartmentState.MTA)
        {
            throw new InvalidOperationException(
                $"Cannot enter an STA apartment scope on managed MTA thread {Environment.CurrentManagedThreadId.ToString(CultureInfo.InvariantCulture)}.");
        }

        if (managedState != ApartmentState.STA && !platform.TryEnsureManagedStaState())
        {
            throw new InvalidOperationException(
                $"Cannot enter an STA apartment scope because thread {Environment.CurrentManagedThreadId.ToString(CultureInfo.InvariantCulture)} could not be configured for STA (managed state: {platform.GetManagedApartmentState()}).");
        }
    }

    private static void VerifyNativeStaAfterComInit(StaPlatform platform)
    {
        var hr = platform.GetApartmentType(out var apartmentType, out var apartmentQualifier);
        if (hr < 0)
        {
            throw new COMException($"CoGetApartmentType failed (HRESULT: 0x{hr.ToString("X8", CultureInfo.InvariantCulture)}).", hr);
        }

        if (!StaPlatform.IsNativeStaApartment(apartmentType, apartmentQualifier))
        {
            throw new InvalidOperationException(
                $"Native COM STA initialization failed on thread {Environment.CurrentManagedThreadId.ToString(CultureInfo.InvariantCulture)}: apartment is {apartmentType.ToString(CultureInfo.InvariantCulture)} (qualifier: {apartmentQualifier.ToString(CultureInfo.InvariantCulture)}) instead of STA.");
        }
    }

    private static void RollbackInitialization(StaPlatform platform, bool comInitialized, bool oleInitialized)
    {
        Exception? ignored = null;
        if (oleInitialized)
        {
            ignored = StaPlatform.InvokeCleanupStep(platform.Uninitialize, ignored);
        }

        if (comInitialized)
        {
            _ = StaPlatform.InvokeCleanupStep(platform.UninitializeCom, ignored);
        }
    }
}
