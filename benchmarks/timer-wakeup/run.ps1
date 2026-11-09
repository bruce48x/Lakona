param(
    [string] $Baseline = 'fc023e86',
    [string] $OutputDirectory = 'artifacts/perf-003/benchmark'
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
Push-Location $root
try {
    $output = [System.IO.Path]::GetFullPath($OutputDirectory, $root)
    New-Item -ItemType Directory -Force $output | Out-Null
    $commit = git rev-parse "$Baseline^{commit}"
    if ($LASTEXITCODE -ne 0) { throw 'Cannot resolve baseline commit.' }
    $source = git show "${commit}:src/Lakona.Game.Server/Hotfix/Timers/LakonaTimerScheduler.cs"
    if ($LASTEXITCODE -ne 0) { throw 'Cannot extract baseline scheduler.' }
    $baselineFile = Join-Path $output 'BaselineTimerScheduler.cs'
    $text = [regex]::Replace(($source -join "`n"), '\bLakonaTimerScheduler\b', 'BaselineTimerScheduler')
    [System.IO.File]::WriteAllText($baselineFile, $text)
    $commit | Set-Content (Join-Path $output 'baseline-commit.txt')
    Copy-Item 'src/Lakona.Game.Server/Hotfix/Timers/LakonaTimerScheduler.cs' (Join-Path $output 'CandidateTimerScheduler.cs')
    Get-FileHash $baselineFile, (Join-Path $output 'CandidateTimerScheduler.cs') |
        Select-Object Path, Hash | ConvertTo-Json | Set-Content (Join-Path $output 'source-hashes.json')
    $artifacts = Join-Path $output 'build'
    dotnet build "$PSScriptRoot/TimerWakeup.csproj" -c Release --artifacts-path $artifacts `
        "-p:TimerBaselineFile=$baselineFile" -p:UseSharedCompilation=false -p:NuGetAudit=false
    if ($LASTEXITCODE -ne 0) { throw 'Benchmark build failed.' }
    $previousTiering = $env:DOTNET_TieredCompilation
    try {
        $env:DOTNET_TieredCompilation = '0'
        dotnet (Join-Path $artifacts 'bin/TimerWakeup/release/BusinessLogic.Tests.dll') `
            (Join-Path $output 'results.json') | Tee-Object (Join-Path $output 'run.log')
        if ($LASTEXITCODE -ne 0) { throw 'Benchmark failed.' }
    }
    finally { $env:DOTNET_TieredCompilation = $previousTiering }
}
finally { Pop-Location }
