using System;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
#if NET8_0_OR_GREATER
using System.Runtime.Versioning;
#endif
using System.Threading;

namespace AdaskoTheBeAsT.Interop.Threading;

// Instance-scoped seam, never a mutable process-wide native hook.
#if NET8_0_OR_GREATER
[SupportedOSPlatform("windows")]
#endif
internal class StaPlatform
{
    public static bool IsNativeStaApartment(int apartmentType, int apartmentQualifier)
    {
        return apartmentType == NativeMethods.APTTYPE_STA
            || apartmentType == NativeMethods.APTTYPE_MAINSTA
            || (apartmentType == NativeMethods.APTTYPE_NA
                && (apartmentQualifier == NativeMethods.APTTYPEQUALIFIER_NA_ON_STA
                    || apartmentQualifier == NativeMethods.APTTYPEQUALIFIER_NA_ON_MAINSTA));
    }

    public static Exception? InvokeCleanupStep(Action step, Exception? primary)
    {
        try
        {
#pragma warning disable CC0031 // Only internally supplied non-null cleanup steps reach this helper.
            step();
#pragma warning restore CC0031
        }
        catch (Exception ex)
        {
            primary ??= ex;
        }

        return primary;
    }

    public void EnsureNativeStaApartment(ref bool comInitialized)
    {
        var hr = GetApartmentType(out var apartmentType, out var apartmentQualifier);
        if (hr == NativeMethods.CO_E_NOTINITIALIZED)
        {
            var initHr = InitializeComSta();
            if (initHr < 0)
            {
                throw new COMException($"CoInitializeEx(COINIT_APARTMENTTHREADED) failed (HRESULT: 0x{initHr.ToString("X8", CultureInfo.InvariantCulture)}).", initHr);
            }

            comInitialized = true;
            hr = GetApartmentType(out apartmentType, out apartmentQualifier);
        }

        if (hr < 0)
        {
            throw new COMException($"CoGetApartmentType failed (HRESULT: 0x{hr.ToString("X8", CultureInfo.InvariantCulture)}).", hr);
        }

        if (!IsNativeStaApartment(apartmentType, apartmentQualifier))
        {
            throw new InvalidOperationException(
                $"Native COM STA initialization failed: worker apartment is {apartmentType.ToString(CultureInfo.InvariantCulture)} (qualifier: {apartmentQualifier.ToString(CultureInfo.InvariantCulture)}) instead of STA. " +
                "On Windows Nano Server, ensure nanoserver_sta_shim.dll is active or configure BeforeThreadStart (for example NanoServerOptions.RefreshStaState) before starting the STA thread.");
        }
    }

    public virtual ApartmentState GetManagedApartmentState()
    {
        return Thread.CurrentThread.GetApartmentState();
    }

    public virtual bool TryEnsureManagedStaState()
    {
        return Thread.CurrentThread.TrySetApartmentState(ApartmentState.STA);
    }

    public virtual void Start(Thread thread)
    {
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    public virtual void Initialize()
    {
        var result = NativeMethods.OleInitialize(IntPtr.Zero);
        if (result < 0)
        {
            throw new COMException($"OleInitialize failed (HRESULT: 0x{result.ToString("X8", CultureInfo.InvariantCulture)}).", result);
        }
    }

    public virtual void Uninitialize()
    {
        NativeMethods.OleUninitialize();
    }

    public virtual int GetApartmentType(out int apartmentType, out int apartmentQualifier)
    {
        if (Environment.OSVersion.Platform != PlatformID.Win32NT)
        {
            apartmentType = NativeMethods.APTTYPE_STA;
            apartmentQualifier = NativeMethods.APTTYPEQUALIFIER_NONE;
            return 0;
        }

        return NativeMethods.CoGetApartmentType(out apartmentType, out apartmentQualifier);
    }

    public virtual int InitializeComSta()
    {
        if (Environment.OSVersion.Platform != PlatformID.Win32NT)
        {
            return 0;
        }

        return NativeMethods.CoInitializeEx(IntPtr.Zero, NativeMethods.COINIT_APARTMENTTHREADED);
    }

    public virtual void UninitializeCom()
    {
        if (Environment.OSVersion.Platform == PlatformID.Win32NT)
        {
            NativeMethods.CoUninitialize();
        }
    }

    public virtual void FreeUnusedLibraries()
    {
        if (Environment.OSVersion.Platform == PlatformID.Win32NT)
        {
            NativeMethods.CoFreeUnusedLibrariesEx(0, 0);
        }
    }

    public virtual void ResetFloatingPointState()
    {
        if (Environment.OSVersion.Platform == PlatformID.Win32NT)
        {
            NativeMethods.ResetFloatingPointExceptionMask();
        }
    }

    public virtual uint GetThreadLocale()
    {
        if (Environment.OSVersion.Platform != PlatformID.Win32NT)
        {
            return 0;
        }

        return NativeMethods.GetThreadLocale();
    }

    public virtual void SetThreadLocale(uint localeId)
    {
        if (Environment.OSVersion.Platform != PlatformID.Win32NT)
        {
            return;
        }

        if (!NativeMethods.SetThreadLocale(localeId))
        {
            var error = Marshal.GetLastWin32Error();
            throw new Win32Exception(error, $"SetThreadLocale(0x{localeId.ToString("X4", CultureInfo.InvariantCulture)}) failed.");
        }
    }

    public virtual PumpOutcome Pump(bool preserveQuit)
    {
        return Pump(preserveQuit, NativeMethods.DefaultMessageBatchSize);
    }

    public virtual PumpOutcome Pump(bool preserveQuit, int maxMessages)
    {
        return NativeMethods.PumpPendingMessages(preserveQuit, maxMessages);
    }

    public virtual uint Wait(IntPtr[] handles)
    {
        var result = NativeMethods.WaitForWork(handles);
        if (result == NativeMethods.WAIT_FAILED)
        {
            // Capture before any other native call can overwrite last error.
            var error = Marshal.GetLastWin32Error();
            throw new Win32Exception(error, "The STA message wait failed.");
        }

        return result;
    }
}
