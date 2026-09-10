param([string] $Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$project = Join-Path $repo 'samples/MutexProbe/MutexProbe.csproj'
dotnet build $project --configuration $Configuration
if ($LASTEXITCODE -ne 0) { throw 'Mutex probe build failed.' }
$probe = Join-Path $repo "samples/MutexProbe/bin/$Configuration/net10.0-windows/MutexProbe.exe"

function Start-Probe([string] $Mode) {
    $start = [System.Diagnostics.ProcessStartInfo]::new($probe)
    $start.UseShellExecute = $false
    foreach ($argument in @($Mode, $name, $readyName, $releaseName)) { $start.ArgumentList.Add($argument) }
    return [System.Diagnostics.Process]::Start($start)
}

function Wait-Probe($Process, [int] $Expected) {
    try {
        if (-not $Process.WaitForExit(10000)) {
            $Process.Kill($true)
            throw 'Mutex probe exceeded its harness deadline.'
        }
        if ($Process.ExitCode -ne $Expected) {
            throw "Expected probe exit $Expected, received $($Process.ExitCode)."
        }
    }
    finally { $Process.Dispose() }
}

foreach ($mode in @('hold', 'abandon')) {
    $name = [Guid]::NewGuid().ToString('N')
    $readyName = [Guid]::NewGuid().ToString('N')
    $releaseName = [Guid]::NewGuid().ToString('N')
    $keeper = [System.Threading.Mutex]::new($false, $name)
    $ready = [System.Threading.EventWaitHandle]::new($false, 'ManualReset', $readyName)
    $release = [System.Threading.EventWaitHandle]::new($false, 'ManualReset', $releaseName)
    $owner = Start-Probe $mode
    try {
        if (-not $ready.WaitOne(5000)) { throw 'Mutex owner did not acquire the lock.' }
        Wait-Probe (Start-Probe 'try') 2
        Wait-Probe (Start-Probe 'cancel') 4
        $release.Set() | Out-Null
        if (-not $owner.WaitForExit(10000)) { throw 'Mutex owner exceeded its deadline.' }
        if ($owner.ExitCode -ne 0) { throw 'Mutex owner failed.' }
        $expected = if ($mode -eq 'abandon') { 3 } else { 0 }
        Wait-Probe (Start-Probe 'try') $expected
        # A failed abandoned acquisition must still release ownership.
        Wait-Probe (Start-Probe 'try') 0
    }
    finally {
        $release.Set() | Out-Null
        if (-not $owner.HasExited) { $owner.Kill($true); $owner.WaitForExit() }
        $owner.Dispose()
        $ready.Dispose()
        $release.Dispose()
        $keeper.Dispose()
    }
}
Write-Host 'Two-process mutex exclusion, timeout, cancellation, abandonment, and release passed.'
