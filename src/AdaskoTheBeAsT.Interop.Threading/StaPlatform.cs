using System;
using System.ComponentModel;
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
            throw new COMException($"OleInitialize failed (HRESULT: 0x{result:X8}).", result);
        }
    }

    public virtual void Uninitialize()
    {
        NativeMethods.OleUninitialize();
    }

    public virtual PumpOutcome Pump(bool preserveQuit)
    {
        return NativeMethods.PumpPendingMessages(preserveQuit);
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
