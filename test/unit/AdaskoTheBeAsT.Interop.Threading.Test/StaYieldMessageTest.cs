using System;
using System.Runtime.InteropServices;
#if NET8_0_OR_GREATER
using System.Runtime.Versioning;
#endif
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Xunit;

namespace AdaskoTheBeAsT.Interop.Threading.Test;

#if NET8_0_OR_GREATER
[SupportedOSPlatform("windows")]
#endif
public class StaYieldMessageTest
{
    [Theory]
    [InlineData(42)]
    [InlineData(-1)]
    public async Task Sleep_PreservesQuitMessageForOuterLoopAsync(int exitCode)
    {
        var result = await SingleThreadedApartmentTask.RunAsync(
            () =>
            {
                NativeMethods.PostQuitMessage(exitCode);
                new StaYield(1).Sleep(25, TestContext.Current.CancellationToken);

                var found = NativeMethods.PeekMessage(out var message, IntPtr.Zero, 0, 0, 1);
                found.Should().BeTrue();
                message.Id.Should().Be(0x0012);
                return unchecked((int)message.WParam.ToUInt64());
            },
            CancellationToken.None).TimeoutAfterAsync(TimeSpan.FromSeconds(5), CancellationToken.None);

        result.Should().Be(exitCode);
    }

    private static class NativeMethods
    {
        // Keep this test fixture compatible with .NET Framework without requiring unsafe code.
#pragma warning disable SYSLIB1054
        [DllImport("user32.dll", ExactSpelling = true)]
        public static extern void PostQuitMessage(int exitCode);

        [DllImport("user32.dll", EntryPoint = "PeekMessageW", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool PeekMessage(
            out Message message,
            IntPtr window,
            uint filterMin,
            uint filterMax,
            uint remove);
#pragma warning restore SYSLIB1054

        [StructLayout(LayoutKind.Sequential)]
        public struct Message
        {
            public readonly IntPtr Window;
            public readonly uint Id;
            public readonly UIntPtr WParam;
            public readonly IntPtr LParam;
            public readonly uint Time;
            public readonly int X;
            public readonly int Y;
            public readonly uint Private;
        }
    }
}
