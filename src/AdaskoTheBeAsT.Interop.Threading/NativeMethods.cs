using System;
#if NET8_0_OR_GREATER
using System.Runtime.CompilerServices;
#endif
using System.Runtime.InteropServices;
#if NET8_0_OR_GREATER
using System.Runtime.Versioning;
#endif

namespace AdaskoTheBeAsT.Interop.Threading;

#pragma warning disable S101
#if NET8_0_OR_GREATER
[SupportedOSPlatform("windows")]
internal static partial class NativeMethods
#else
internal static class NativeMethods
#endif
{
    public const uint INFINITE = unchecked((uint)-1);
    public const uint QS_ALLINPUT = 0x04FF;
    public const uint WAIT_OBJECT_0 = 0;
    public const uint WAIT_FAILED = uint.MaxValue;
    public const uint COINIT_APARTMENTTHREADED = 0x2;
    public const int CO_E_NOTINITIALIZED = unchecked((int)0x800401F0);
    public const int RPC_E_CHANGED_MODE = unchecked((int)0x80010106);
    public const int APTTYPE_STA = 0;
    public const int APTTYPE_MTA = 1;
    public const int APTTYPE_NA = 2;
    public const int APTTYPE_MAINSTA = 3;
    public const int APTTYPEQUALIFIER_NONE = 0;
    public const int APTTYPEQUALIFIER_IMPLICIT_MTA = 1;
    public const int APTTYPEQUALIFIER_NA_ON_STA = 3;
    public const int APTTYPEQUALIFIER_NA_ON_MAINSTA = 5;
    public const int DefaultMessageBatchSize = 64;
    private const uint PM_REMOVE = 1;
    private const uint WM_QUIT = 0x0012;
    private const uint MWMO_INPUTAVAILABLE = 0x0004;
    private const uint McwEm = 0x0008001F;

    public static PumpOutcome PumpPendingMessages(bool preserveQuit = true, int maxMessages = DefaultMessageBatchSize)
    {
        if (maxMessages <= 0)
        {
            return default;
        }

        for (var count = 0; count < maxMessages; count++)
        {
            if (!PeekMessage(out var msg, IntPtr.Zero, 0, 0, PM_REMOVE))
            {
                return default;
            }

            if (msg.message == WM_QUIT)
            {
                var exitCode = unchecked((int)msg.wParam.ToUInt64());
                if (preserveQuit)
                {
                    PostQuitMessage(exitCode);
                }

                return new PumpOutcome(quitSeen: true, exitCode, budgetExhausted: false);
            }

            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }

        return new PumpOutcome(quitSeen: false, exitCode: 0, budgetExhausted: true);
    }

    public static uint WaitForWork(IntPtr[] handles)
    {
        return MsgWaitForMultipleObjectsEx((uint)handles.Length, handles, INFINITE, QS_ALLINPUT, MWMO_INPUTAVAILABLE);
    }

    public static void ResetFloatingPointExceptionMask()
    {
        if (RuntimeInformation.ProcessArchitecture != Architecture.X86)
        {
            return;
        }

        // Clear sticky x87/SSE status flags first (non-waiting fnclex + MXCSR status clear)
        // before restoring all 6 exception masks (_MCW_EM), leaving rounding and precision untouched.
        _ = ClearFloatingPointStatus();
        _ = Control87Sse2(McwEm, McwEm, out _, out _);
    }

#if NET8_0_OR_GREATER
    [LibraryImport("ole32.dll")]
    public static partial int OleInitialize(IntPtr pvReserved);

    [LibraryImport("ole32.dll")]
    public static partial void OleUninitialize();

    [LibraryImport("combase.dll")]
    public static partial int CoGetApartmentType(out int pAptType, out int pAptQualifier);

    [LibraryImport("combase.dll")]
    public static partial int CoInitializeEx(IntPtr pvReserved, uint dwCoInit);

    [LibraryImport("combase.dll")]
    public static partial void CoUninitialize();

    [LibraryImport("combase.dll")]
    public static partial void CoFreeUnusedLibrariesEx(uint dwUnloadDelay, uint dwReserved);

    [LibraryImport("kernel32.dll")]
    public static partial uint GetThreadLocale();

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetThreadLocale(uint locale);

    [LibraryImport("ucrtbase.dll", EntryPoint = "_clearfp")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial uint ClearFloatingPointStatus();

    [LibraryImport("ucrtbase.dll", EntryPoint = "__control87_2")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial int Control87Sse2(uint newValue, uint mask, out uint x86ControlWord, out uint sse2ControlWord);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial uint MsgWaitForMultipleObjectsEx(
        uint nCount, [In] IntPtr[] pHandles, uint dwMilliseconds, uint dwWakeMask, uint dwFlags);

    [LibraryImport("user32.dll", EntryPoint = "PeekMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PeekMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

    [LibraryImport("user32.dll", EntryPoint = nameof(TranslateMessage))]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TranslateMessage(ref MSG lpMsg);

    [LibraryImport("user32.dll", EntryPoint = "DispatchMessageW")]
    private static partial IntPtr DispatchMessage(ref MSG lpMsg);

    [LibraryImport("user32.dll")]
    private static partial void PostQuitMessage(int exitCode);
#else
    [DllImport("ole32.dll")]
    public static extern int OleInitialize(IntPtr pvReserved);

    [DllImport("ole32.dll")]
    public static extern void OleUninitialize();

    [DllImport("combase.dll", ExactSpelling = true)]
    public static extern int CoGetApartmentType(out int pAptType, out int pAptQualifier);

    [DllImport("combase.dll", ExactSpelling = true)]
    public static extern int CoInitializeEx(IntPtr pvReserved, uint dwCoInit);

    [DllImport("combase.dll", ExactSpelling = true)]
    public static extern void CoUninitialize();

    [DllImport("combase.dll", ExactSpelling = true)]
    public static extern void CoFreeUnusedLibrariesEx(uint dwUnloadDelay, uint dwReserved);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    public static extern uint GetThreadLocale();

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetThreadLocale(uint locale);

    [DllImport("ucrtbase.dll", EntryPoint = "_clearfp", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern uint ClearFloatingPointStatus();

    [DllImport("ucrtbase.dll", EntryPoint = "__control87_2", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern int Control87Sse2(uint newValue, uint mask, out uint x86ControlWord, out uint sse2ControlWord);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint MsgWaitForMultipleObjectsEx(
        uint nCount, [In] IntPtr[] pHandles, uint dwMilliseconds, uint dwWakeMask, uint dwFlags);

    [DllImport("user32.dll", EntryPoint = "PeekMessageW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

    [DllImport("user32.dll", EntryPoint = nameof(TranslateMessage), CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage([In] ref MSG lpMsg);

    [DllImport("user32.dll", EntryPoint = "DispatchMessageW", CharSet = CharSet.Unicode)]
    private static extern IntPtr DispatchMessage([In] ref MSG lpMsg);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern void PostQuitMessage(int exitCode);
#endif

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public readonly IntPtr hwnd;
        public readonly uint message;
        public readonly UIntPtr wParam;
        public readonly IntPtr lParam;
        public readonly uint time;
        public readonly POINT pt;
        public readonly uint lPrivate;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public readonly int x;
        public readonly int y;
    }
}
#pragma warning restore S101
