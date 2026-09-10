using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
#if NET8_0_OR_GREATER
using System.Runtime.Versioning;
#endif

namespace AdaskoTheBeAsT.Interop.Threading.Test;

// One message-only window, created and disposed on its owning STA.
// Window destruction must run on the owning STA, never on the finalizer thread.
#pragma warning disable CA2216
#if NET8_0_OR_GREATER
[SupportedOSPlatform("windows")]
#endif
internal sealed class MessageWindow : IDisposable
{
    public const uint TestMessage = 0x8001;
    private readonly string _className = Guid.NewGuid().ToString("N");
    private readonly WindowProcedure _procedure;
    private readonly IntPtr _window;

    public MessageWindow()
    {
        _procedure = Dispatch;
        var windowClass = new WindowClass
        {
            ClassName = _className,
            Procedure = Marshal.GetFunctionPointerForDelegate(_procedure),
        };
        if (NativeMethods.RegisterClass(ref windowClass) == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        _window = NativeMethods.CreateWindowEx(0, _className, string.Empty, 0, 0, 0, 0, 0, new IntPtr(-3), IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (_window == IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            NativeMethods.UnregisterClass(_className, IntPtr.Zero);
            throw new Win32Exception(error);
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr WindowProcedure(IntPtr window, uint message, UIntPtr wParam, IntPtr lParam);

    public int Received { get; private set; }

    public bool Repost { get; set; }

    public void Post()
    {
        if (!NativeMethods.PostMessage(_window, TestMessage, UIntPtr.Zero, IntPtr.Zero))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    public void Dispose()
    {
        NativeMethods.DestroyWindow(_window);
        NativeMethods.UnregisterClass(_className, IntPtr.Zero);
        GC.KeepAlive(_procedure);
    }

    private IntPtr Dispatch(IntPtr window, uint message, UIntPtr wParam, IntPtr lParam)
    {
        if (message == TestMessage)
        {
            Received++;
            if (Repost)
            {
                // Never throw across the unmanaged callback boundary.
                NativeMethods.PostMessage(window, TestMessage, UIntPtr.Zero, IntPtr.Zero);
            }

            return IntPtr.Zero;
        }

        return NativeMethods.DefWindowProc(window, message, wParam, lParam);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        public uint Style;
        public IntPtr Procedure;
        public int ClassExtra;
        public int WindowExtra;
        public IntPtr Instance;
        public IntPtr Icon;
        public IntPtr Cursor;
        public IntPtr Background;
        public string? MenuName;
        public string ClassName;
    }

    private static class NativeMethods
    {
#pragma warning disable SYSLIB1054 // One fixture supports both Framework and modern .NET.
        [DllImport("user32.dll", EntryPoint = "RegisterClassW", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern ushort RegisterClass(ref WindowClass windowClass);

        [DllImport("user32.dll", EntryPoint = "UnregisterClassW", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnregisterClass(string className, IntPtr instance);

        [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr CreateWindowEx(
            uint extendedStyle, string className, string title, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DestroyWindow(IntPtr window);

        [DllImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool PostMessage(IntPtr window, uint message, UIntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", EntryPoint = "DefWindowProcW")]
        public static extern IntPtr DefWindowProc(IntPtr window, uint message, UIntPtr wParam, IntPtr lParam);

#pragma warning restore SYSLIB1054
    }
}
