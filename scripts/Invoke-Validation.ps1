param(
    [ValidateSet('Windows', 'Portable', 'X86')]
    [string] $Mode = 'Windows',
    [string] $Configuration = 'Release'
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$tests = Join-Path $repo 'test/unit/AdaskoTheBeAsT.Interop.Threading.Test/AdaskoTheBeAsT.Interop.Threading.Test.csproj'
$solution = Join-Path $repo 'AdaskoTheBeAsT.Interop.Threading.slnx'

function Invoke-Checked([string] $Executable, [string[]] $Arguments, [int] $DeadlineSeconds = 300) {
    $start = [System.Diagnostics.ProcessStartInfo]::new($Executable)
    $start.WorkingDirectory = $repo
    $start.UseShellExecute = $false
    foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    $process = [System.Diagnostics.Process]::Start($start)
    try {
        if (-not $process.WaitForExit($DeadlineSeconds * 1000)) {
            $process.Kill($true)
            $process.WaitForExit()
            throw "$Executable exceeded its $DeadlineSeconds-second deadline."
        }
        if ($process.ExitCode -ne 0) { throw "$Executable failed with exit code $($process.ExitCode)." }
    }
    finally { $process.Dispose() }
}

if ($Mode -ne 'Portable' -and -not $IsWindows) { throw "$Mode validation requires Windows and PowerShell 7." }
Invoke-Checked dotnet @('restore', $solution, '--locked-mode')
if ($Mode -eq 'Portable') {
    Invoke-Checked dotnet @('test', $tests, '-c', $Configuration, '-f', 'net10.0', '--no-restore',
        '--filter', 'FullyQualifiedName~TaskExtension', '--logger', 'trx')
    return
}

$previousArchitecture = $env:ASTRA_TEST_ARCHITECTURE
try {
    if ($Mode -eq 'X86') {
        $env:ASTRA_TEST_ARCHITECTURE = 'x86'
        Invoke-Checked dotnet @('test', $tests, '-c', $Configuration, '-f', 'net462', '--no-restore',
            '-p:PlatformTarget=x86', '--logger', 'trx', '--', 'RunConfiguration.TargetPlatform=x86')
        return
    }

    $env:ASTRA_TEST_ARCHITECTURE = 'x64'
    # GeneratePackageOnBuild is enabled. Build first; pack alone can reuse stale assemblies.
    Invoke-Checked dotnet @('build', $solution, '-c', $Configuration, '--no-restore', '-p:ContinuousIntegrationBuild=true')
    foreach ($tfm in @('net10.0', 'net9.0', 'net8.0', 'net481', 'net48', 'net472', 'net471', 'net47', 'net462')) {
        Invoke-Checked dotnet @('test', $tests, '-c', $Configuration, '-f', $tfm, '--no-build',
            '--logger', 'trx', '--', 'RunConfiguration.TargetPlatform=x64') 90
    }
    foreach ($sample in @('StaService', 'MutexProbe')) {
        $project = Join-Path $repo "samples/$sample/$sample.csproj"
        Invoke-Checked dotnet @('restore', $project, '--locked-mode')
        Invoke-Checked dotnet @('build', $project, '-c', $Configuration, '--no-restore')
    }
    Invoke-Checked dotnet @('run', '--project', (Join-Path $repo 'samples/StaService/StaService.csproj'),
        '-c', $Configuration, '--no-build') 60
    Invoke-Checked pwsh @('-NoProfile', '-File', (Join-Path $repo 'scripts/Test-MutexProcesses.ps1'),
        '-Configuration', $Configuration) 90
}
finally { $env:ASTRA_TEST_ARCHITECTURE = $previousArchitecture }
