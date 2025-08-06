using System;
using System.Runtime.InteropServices;

namespace AdaskoTheBeAsT.Interop.Threading;

#pragma warning disable S101
internal static class NativeMethods
{
    public const uint INFINITE = unchecked((uint)-1);
    public const uint QS_ALLINPUT = 0x04FF;
    public const uint WAIT_OBJECT_0 = 0x00000000;
    private const uint PM_REMOVE = 0x0001;

    public static void PumpPendingMessages()
    {
        MSG msg;
        while (PeekMessage(out msg, IntPtr.Zero, 0, 0, PM_REMOVE))
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint MsgWaitForMultipleObjects(
        uint nCount,
        IntPtr[] pHandles,
        bool bWaitAll,
        uint dwMilliseconds,
        uint dwWakeMask);

    [DllImport("user32.dll")]
    private static extern bool PeekMessage(
        out MSG lpMsg,
        IntPtr hWnd,
        uint wMsgFilterMin,
        uint wMsgFilterMax,
        uint wRemoveMsg);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage([In] ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage([In] ref MSG lpMsg);

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public UIntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
        public uint lPrivate;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }
}
#pragma warning restore S101
