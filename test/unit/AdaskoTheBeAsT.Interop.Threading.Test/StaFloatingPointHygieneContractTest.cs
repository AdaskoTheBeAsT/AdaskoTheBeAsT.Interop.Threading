using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
#if NET8_0_OR_GREATER
using System.Runtime.Versioning;
#endif
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Xunit;

namespace AdaskoTheBeAsT.Interop.Threading.Test;

#pragma warning disable CA2216, VSTHRD003
#if NET8_0_OR_GREATER
[SupportedOSPlatform("windows")]
#endif
public class StaFloatingPointHygieneContractTest
{
    private const uint McwEm = 0x0008001F;
    private const uint McwRc = 0x00000300;
    private const uint McwPc = 0x00030000;
    private const uint RcUp = 0x00000200;
    private const uint Pc64 = 0x00000000;

    [Fact]
    public async Task ResetFloatingPointMaskAfterWorkItem_IsOptIn_AndInvokedAfterScopeAndWorkItemsAsync()
    {
        var defaultPlatform = new FpuRecordingPlatform();
        using (var defaultScheduler = new SingleThreadedApartmentTaskScheduler(
            options: null,
            defaultPlatform,
            TimeSpan.FromSeconds(5)))
        {
            (await defaultScheduler.RunAsync(() => 1, CancellationToken.None)).Should().Be(1);
        }

        defaultPlatform.Events.Should().BeEmpty();

        var enabledPlatform = new FpuRecordingPlatform();
        var options = new SingleThreadedApartmentTaskSchedulerOptions
        {
            CreateThreadScope = () =>
            {
                enabledPlatform.Events.Add("scope-create");
                return null;
            },
            ResetFloatingPointMaskAfterWorkItem = true,
        };

        using (var enabledScheduler = new SingleThreadedApartmentTaskScheduler(
            options,
            enabledPlatform,
            TimeSpan.FromSeconds(5)))
        {
            await enabledScheduler.RunAsync(
                () =>
                {
                    enabledPlatform.Events.Add("work-1");
                    return 1;
                },
                CancellationToken.None);
            await enabledScheduler.RunAsync(
                () =>
                {
                    enabledPlatform.Events.Add("work-2");
                    return 2;
                },
                CancellationToken.None);
        }

        enabledPlatform.Events.Should().Equal("reset", "scope-create", "reset", "work-1", "reset", "work-2", "reset");
    }

    [Fact]
    public async Task NativeX86TrapPoisoning_WithoutOptInFaults_WithOptInClearsPendingAndPreservesRoundingAndPrecisionAsync()
    {
        if (RuntimeInformation.ProcessArchitecture != Architecture.X86)
        {
            // On x64, verify ResetFloatingPointMaskAfterWorkItem = true is a safe no-op.
            using var x64Scheduler = new SingleThreadedApartmentTaskScheduler(
                new SingleThreadedApartmentTaskSchedulerOptions { ResetFloatingPointMaskAfterWorkItem = true });
            var x64Nan = await x64Scheduler.RunAsync(() => DivideValues(0.0, 0.0), CancellationToken.None);
            double.IsNaN(x64Nan).Should().BeTrue();
            return;
        }

        using var fixture = new X86TrapPoisonFixture();
        await VerifyNegativeControlWithoutOptInOnX86Async(fixture);
        await VerifyPositiveControlAndModePreservationOnX86Async(fixture);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static double DivideValues(double numerator, double denominator) => numerator / denominator;

    private static async Task VerifyNegativeControlWithoutOptInOnX86Async(X86TrapPoisonFixture fixture)
    {
        using var unguarded = new SingleThreadedApartmentTaskScheduler(
            new SingleThreadedApartmentTaskSchedulerOptions { ResetFloatingPointMaskAfterWorkItem = false });

        await unguarded.RunAsync(
            () =>
            {
                fixture.UnmaskDelphiTraps();
                return 0;
            },
            CancellationToken.None);

#if NET8_0_OR_GREATER
        // CoreCLR win-x86 translates unmasked hardware FP traps into Environment.FailFast (RhThrowHwEx);
        // scripts/Test-FpuProcess.ps1 exercises that process-terminating fault in an isolated child process,
        // while in-process xUnit verifies the unmasked control words persist into the next work item.
        var (unguardedX86Cw, unguardedSse2Cw) = await unguarded.RunAsync(
            () =>
            {
                _ = NativeMethods.Control87Sse2(0, 0, out var x86Cw, out var sse2Cw);
                return (x86Cw, sse2Cw);
            },
            CancellationToken.None);
        (unguardedX86Cw & McwEm).Should().NotBe(McwEm);
        (unguardedSse2Cw & McwEm).Should().NotBe(McwEm);
#else
        var trapError = await Record.ExceptionAsync(
            async () => await unguarded.RunAsync(() => DivideValues(0.0, 0.0), CancellationToken.None));
        trapError.Should().BeAssignableTo<ArithmeticException>();
#endif

        // Clean up the unguarded worker's FPU state before disposing it.
        await unguarded.RunAsync(
            () =>
            {
                AdaskoTheBeAsT.Interop.Threading.NativeMethods.ResetFloatingPointExceptionMask();
                return 0;
            },
            CancellationToken.None);
    }

    private static async Task VerifyPositiveControlAndModePreservationOnX86Async(X86TrapPoisonFixture fixture)
    {
        using var guarded = new SingleThreadedApartmentTaskScheduler(
            new SingleThreadedApartmentTaskSchedulerOptions
            {
                EnableDiagnostics = true,
                ResetFloatingPointMaskAfterWorkItem = true,
                CreateThreadScope = () =>
                {
                    // Set caller-chosen upward rounding + 64-bit precision, then arm pending x87 + SSE traps!
                    _ = NativeMethods.Control87Sse2(RcUp | Pc64, McwRc | McwPc, out _, out _);
                    fixture.ArmPendingDivideByZeroAndUnmaskAllKeepUpward64();
                    return null;
                },
            });

        // Work item 1 runs after CreateThreadScope armed pending traps; must succeed and preserve RC_UP + PC_64.
        var first = await guarded.RunAsync(
            () =>
            {
                _ = NativeMethods.Control87Sse2(0, 0, out var x86Cw, out var sse2Cw);
                var nan = DivideValues(0.0, 0.0);
                var inf = DivideValues(1.0, 0.0);
                var subnormalOk = DivideValues(double.Epsilon, 2.0) >= 0.0;
                var isNan = double.IsNaN(nan);
                var isInf = double.IsPositiveInfinity(inf);
                fixture.ArmPendingDivideByZeroAndUnmaskAllKeepUpward64();
                return (x86Cw, sse2Cw, IsNan: isNan, IsInf: isInf, SubnormalOk: subnormalOk);
            },
            CancellationToken.None);

        (first.x86Cw & McwEm).Should().Be(McwEm);
        (first.sse2Cw & McwEm).Should().Be(McwEm);
        (first.x86Cw & McwRc).Should().Be(RcUp);
        (first.sse2Cw & McwRc).Should().Be(RcUp);
        (first.x86Cw & McwPc).Should().Be(Pc64);
        first.IsNan.Should().BeTrue();
        first.IsInf.Should().BeTrue();
        first.SubnormalOk.Should().BeTrue();

        // Work item 2 runs after Work item 1 armed pending x87 + SSE traps again; must also succeed and retain modes.
        var second = await guarded.RunAsync(
            () =>
            {
                _ = NativeMethods.Control87Sse2(0, 0, out var x86Cw, out var sse2Cw);
                var nan = DivideValues(0.0, 0.0);
                var inexact = DivideValues(1.0, 3.0);
                return (x86Cw, sse2Cw, IsNan: double.IsNaN(nan), InexactOk: inexact > 0.3);
            },
            CancellationToken.None);

        (second.x86Cw & McwEm).Should().Be(McwEm);
        (second.sse2Cw & McwEm).Should().Be(McwEm);
        (second.x86Cw & McwRc).Should().Be(RcUp);
        (second.sse2Cw & McwRc).Should().Be(RcUp);
        (second.x86Cw & McwPc).Should().Be(Pc64);
        second.IsNan.Should().BeTrue();
        second.InexactOk.Should().BeTrue();
    }

    private static class NativeMethods
    {
#pragma warning disable SYSLIB1054
        [DllImport("ucrtbase.dll", EntryPoint = "__control87_2", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern int Control87Sse2(uint newValue, uint mask, out uint x86ControlWord, out uint sse2ControlWord);

        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        public static extern IntPtr VirtualAlloc(IntPtr lpAddress, UIntPtr dwSize, uint flAllocationType, uint flProtect);

        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool VirtualProtect(IntPtr lpAddress, UIntPtr dwSize, uint flNewProtect, out uint lpflOldProtect);

        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool VirtualFree(IntPtr lpAddress, UIntPtr dwSize, uint dwFreeType);
#pragma warning restore SYSLIB1054
    }

    private sealed class FpuRecordingPlatform : StaPlatform
    {
        public List<string> Events { get; } = [];

        public override void ResetFloatingPointState()
        {
            Events.Add("reset");
            base.ResetFloatingPointState();
        }
    }

    // Allocates W^X (RW -> RX) native x86 stubs that execute real x87 + SSE2 hardware instructions.
    private sealed class X86TrapPoisonFixture : IDisposable
    {
        private const uint MemCommitReserve = 0x3000;
        private const uint MemRelease = 0x8000;
        private const uint PageReadWrite = 0x04;
        private const uint PageExecuteRead = 0x20;

        // Delphi-style CW 0x1332 (#IE, #ZE, #OE unmasked) + SSE2 MXCSR 0x1900 (#IE, #ZE, #OE unmasked).
        private static readonly byte[] UnmaskDelphiCode =
        [
            0x55, 0x89, 0xE5, 0x83, 0xEC, 0x08,
            0x66, 0xC7, 0x45, 0xFC, 0x32, 0x13,
            0xD9, 0x6D, 0xFC,
            0xC7, 0x45, 0xF8, 0x00, 0x19, 0x00, 0x00,
            0x0F, 0xAE, 0x55, 0xF8,
            0x89, 0xEC, 0x5D, 0xC3,
        ];

        // Computes 1.0 / 0.0 while masked (setting sticky FSW.ZE = 1), then loads x87 CW 0x0B00
        // (all 6 x87 traps unmasked, PC=64-bit, RC=UP) via non-waiting fldcw + MXCSR 0x00004004
        // (sticky SSE ZE=1, all 6 SSE masks=0 unmasked, RC=UP), returning with armed pending traps!
        private static readonly byte[] ArmPendingAndUnmaskAllCode =
        [
            0x55, 0x89, 0xE5, 0x83, 0xEC, 0x08,
            0xD9, 0xE8,
            0xD9, 0xEE,
            0xDE, 0xF9,
            0xDD, 0xD8,
            0x66, 0xC7, 0x45, 0xFC, 0x00, 0x0B,
            0xD9, 0x6D, 0xFC,
            0xC7, 0x45, 0xF8, 0x04, 0x40, 0x00, 0x00,
            0x0F, 0xAE, 0x55, 0xF8,
            0x89, 0xEC, 0x5D, 0xC3,
        ];

        private readonly NativeAction _unmaskDelphi;
        private readonly NativeAction _armPendingAndUnmaskAll;
        private IntPtr _buffer;

        public X86TrapPoisonFixture()
        {
            const int SecondOffset = 64;
            const int TotalBytes = 128;
            _buffer = NativeMethods.VirtualAlloc(IntPtr.Zero, (UIntPtr)TotalBytes, MemCommitReserve, PageReadWrite);
            if (_buffer == IntPtr.Zero)
            {
                throw new InvalidOperationException("VirtualAlloc failed for x86 FPU fixture.");
            }

            Marshal.Copy(UnmaskDelphiCode, 0, _buffer, UnmaskDelphiCode.Length);
            Marshal.Copy(ArmPendingAndUnmaskAllCode, 0, IntPtr.Add(_buffer, SecondOffset), ArmPendingAndUnmaskAllCode.Length);
            if (!NativeMethods.VirtualProtect(_buffer, (UIntPtr)TotalBytes, PageExecuteRead, out _))
            {
                NativeMethods.VirtualFree(_buffer, UIntPtr.Zero, MemRelease);
                _buffer = IntPtr.Zero;
                throw new InvalidOperationException("VirtualProtect(PAGE_EXECUTE_READ) failed for x86 FPU fixture.");
            }

            _unmaskDelphi = Marshal.GetDelegateForFunctionPointer<NativeAction>(_buffer);
            _armPendingAndUnmaskAll = Marshal.GetDelegateForFunctionPointer<NativeAction>(IntPtr.Add(_buffer, SecondOffset));
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void NativeAction();

        public void UnmaskDelphiTraps() => _unmaskDelphi();

        public void ArmPendingDivideByZeroAndUnmaskAllKeepUpward64() => _armPendingAndUnmaskAll();

        public void Dispose()
        {
            var ptr = Interlocked.Exchange(ref _buffer, IntPtr.Zero);
            if (ptr != IntPtr.Zero)
            {
                _ = NativeMethods.VirtualFree(ptr, UIntPtr.Zero, MemRelease);
            }
        }
    }
}
