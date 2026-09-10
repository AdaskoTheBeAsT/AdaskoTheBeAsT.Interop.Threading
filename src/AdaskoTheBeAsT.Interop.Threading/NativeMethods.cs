using System;
using System.Runtime.InteropServices;
#if NET8_0_OR_GREATER
using System.Runtime.Versioning;
#endif

namespace AdaskoTheBeAsT.Interop.Threading;

#pragma warning disable S101
#if NET8_0_OR_GREATER
[SupportedOSPlatform("windows")]
#endif
internal static partial class NativeMethods
{
    public const uint INFINITE = unchecked((uint)-1);
    public const uint QS_ALLINPUT = 0x04FF;
    public const uint WAIT_OBJECT_0 = 0;
    public const uint WAIT_FAILED = uint.MaxValue;
    private const uint PM_REMOVE = 1;
    private const uint WM_QUIT = 0x0012;
    private const uint MWMO_INPUTAVAILABLE = 0x0004;
    private const int MessageBatchSize = 64;

    public static PumpOutcome PumpPendingMessages(bool preserveQuit = true)
    {
        for (var count = 0; count < MessageBatchSize; count++)
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

#if NET8_0_OR_GREATER
    [LibraryImport("ole32.dll")]
    public static partial int OleInitialize(IntPtr pvReserved);

    [LibraryImport("ole32.dll")]
    public static partial void OleUninitialize();

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial uint MsgWaitForMultipleObjectsEx(
        uint nCount, IntPtr[] pHandles, uint dwMilliseconds, uint dwWakeMask, uint dwFlags);

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
