param([string] $Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$libraryProject = Join-Path $repo 'src/AdaskoTheBeAsT.Interop.Threading/AdaskoTheBeAsT.Interop.Threading.csproj'
$nativeSource = Join-Path $repo 'test/native/fpu_poison.c'
$nativeOutDir = Join-Path ([System.IO.Path]::GetTempPath()) "astra-fpu-x86-$([System.Guid]::NewGuid().ToString('N'))"
$nativeDll = Join-Path $nativeOutDir 'fpu_poison_x86.dll'
$nativeObj = Join-Path $nativeOutDir 'fpu_poison.obj'

dotnet build $libraryProject -c $Configuration -f net472
if ($LASTEXITCODE -ne 0) { throw 'Library net472 build failed.' }
$libraryDll = (Resolve-Path (Join-Path $repo "src/AdaskoTheBeAsT.Interop.Threading/bin/$Configuration/net472/AdaskoTheBeAsT.Interop.Threading.dll")).Path

# Locate Visual Studio vcvarsall.bat to compile the real 32-bit C fixture when installed.
$vcvarsCandidates = Get-ChildItem 'C:\Program Files\Microsoft Visual Studio' -Recurse -Filter 'vcvarsall.bat' -ErrorAction SilentlyContinue |
    Select-Object -ExpandProperty FullName
if (-not $vcvarsCandidates) {
    throw 'MSVC vcvarsall.bat was not found; cannot compile real x86 native FPU fixture.'
}

$vcvars = $vcvarsCandidates | Select-Object -Last 1
New-Item -ItemType Directory -Force -Path $nativeOutDir | Out-Null
try {
$compileCmd = "`"$vcvars`" x86 && cl.exe /nologo /LD /O2 /GS- `"$nativeSource`" /Fe:`"$nativeDll`" /Fo:`"$nativeObj`""
cmd.exe /d /c $compileCmd
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $nativeDll)) {
    throw 'Failed to compile fpu_poison_x86.dll.'
}

$x86Powershell = Join-Path $env:SystemRoot 'SysWOW64\WindowsPowerShell\v1.0\powershell.exe'
if (-not (Test-Path $x86Powershell)) {
    throw '32-bit SysWOW64 WindowsPowerShell was not found.'
}

function Invoke-FreshX86Probe([string] $Mode) {
    $script = @'
param([string] $LibPath, [string] $DllPath, [string] $ProbeMode)
$ErrorActionPreference = 'Stop'
if ([IntPtr]::Size -ne 4) { throw "Expected 32-bit process, got IntPtr.Size=$([IntPtr]::Size)" }
[void][System.Reflection.Assembly]::LoadFrom($LibPath)
$escapedDll = $DllPath.Replace('\', '\\')
$code = @"
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using AdaskoTheBeAsT.Interop.Threading;

public static class NativeFpuRunner
{
    private const uint McwEm = 0x0008001Fu;
    private const uint McwRc = 0x00000300u;
    private const uint McwPc = 0x00030000u;
    private const uint RcUp = 0x00000200u;
    private const uint Pc64 = 0x00000000u;

    [DllImport("$escapedDll", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern void fpu_poison_unmask_delphi();

    [DllImport("$escapedDll", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern void fpu_poison_arm_pending_and_unmask_all_upward64();

    [DllImport("ucrtbase.dll", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern uint _clearfp();

    [DllImport("ucrtbase.dll", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern int __control87_2(uint newValue, uint mask, out uint x86Cw, out uint sse2Cw);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static double Div(double a, double b) { return a / b; }

    public static int Run(string mode)
    {
        if (mode == "unmasked-faults")
        {
            var opts = new SingleThreadedApartmentTaskSchedulerOptions { ResetFloatingPointMaskAfterWorkItem = false };
            using (var scheduler = new SingleThreadedApartmentTaskScheduler(opts))
            {
                scheduler.RunAsync(() => { fpu_poison_unmask_delphi(); return 0; }, CancellationToken.None).GetAwaiter().GetResult();
                try
                {
                    scheduler.RunAsync(() => { return Div(0.0, 0.0); }, CancellationToken.None).GetAwaiter().GetResult();
                    return 10;
                }
                catch (ArithmeticException)
                {
                    scheduler.RunAsync(() =>
                    {
                        uint x, s;
                        _clearfp();
                        __control87_2(McwEm, McwEm, out x, out s);
                        return 0;
                    }, CancellationToken.None).GetAwaiter().GetResult();
                    return 0;
                }
            }
        }

        if (mode == "hygiene-recovers")
        {
            var opts = new SingleThreadedApartmentTaskSchedulerOptions
            {
                EnableDiagnostics = true,
                ResetFloatingPointMaskAfterWorkItem = true,
                CreateThreadScope = () =>
                {
                    uint d1, d2;
                    __control87_2(RcUp | Pc64, McwRc | McwPc, out d1, out d2);
                    fpu_poison_arm_pending_and_unmask_all_upward64();
                    return null;
                }
            };
            using (var scheduler = new SingleThreadedApartmentTaskScheduler(opts))
            {
                bool ok1 = scheduler.RunAsync(() =>
                {
                    uint x86Cw, sse2Cw;
                    __control87_2(0, 0, out x86Cw, out sse2Cw);
                    double nan = Div(0.0, 0.0);
                    double inf = Div(1.0, 0.0);
                    double sub = Div(double.Epsilon, 2.0);
                    bool valid = ((x86Cw & McwEm) == McwEm) && ((sse2Cw & McwEm) == McwEm)
                        && ((x86Cw & McwRc) == RcUp) && ((sse2Cw & McwRc) == RcUp)
                        && ((x86Cw & McwPc) == Pc64)
                        && double.IsNaN(nan) && double.IsPositiveInfinity(inf) && (sub >= 0.0);
                    fpu_poison_arm_pending_and_unmask_all_upward64();
                    return valid;
                }, CancellationToken.None).GetAwaiter().GetResult();
                if (!ok1) { return 21; }

                bool ok2 = scheduler.RunAsync(() =>
                {
                    uint x86Cw, sse2Cw;
                    __control87_2(0, 0, out x86Cw, out sse2Cw);
                    double nan = Div(0.0, 0.0);
                    double third = Div(1.0, 3.0);
                    return ((x86Cw & McwEm) == McwEm) && ((sse2Cw & McwEm) == McwEm)
                        && ((x86Cw & McwRc) == RcUp) && ((sse2Cw & McwRc) == RcUp)
                        && ((x86Cw & McwPc) == Pc64)
                        && double.IsNaN(nan) && (third > 0.3);
                }, CancellationToken.None).GetAwaiter().GetResult();
                return ok2 ? 0 : 22;
            }
        }

        return 99;
    }
}
"@
Add-Type -TypeDefinition $code -ReferencedAssemblies $LibPath
exit ([NativeFpuRunner]::Run($ProbeMode))
'@

    $tempScript = Join-Path $nativeOutDir "run-$Mode.ps1"
    Set-Content -LiteralPath $tempScript -Value $script -Encoding UTF8
    $psi = [System.Diagnostics.ProcessStartInfo]::new($x86Powershell)
    $psi.UseShellExecute = $false
    foreach ($arg in @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $tempScript, $libraryDll, $nativeDll, $Mode)) {
        $psi.ArgumentList.Add($arg)
    }

    $proc = [System.Diagnostics.Process]::Start($psi)
    try {
        if (-not $proc.WaitForExit(30000)) {
            $proc.Kill($true)
            throw "x86 FPU probe ($Mode) timed out."
        }
        if ($proc.ExitCode -ne 0) {
            throw "x86 FPU probe ($Mode) failed with exit code $($proc.ExitCode)."
        }
    }
    finally {
        $proc.Dispose()
    }
}

Invoke-FreshX86Probe 'unmasked-faults'
Invoke-FreshX86Probe 'hygiene-recovers'
Write-Host 'Fresh x86 process native C FPU trap-poisoning and hygiene checks passed.'
}
finally {
    Remove-Item -LiteralPath $nativeOutDir -Recurse -Force -ErrorAction SilentlyContinue
}
