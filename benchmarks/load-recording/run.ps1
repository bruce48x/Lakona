param(
    [string] $Baseline = '7738a230',
    [string] $OutputDirectory = 'artifacts/ops-001/recording'
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
Push-Location $root
try {
    $output = [System.IO.Path]::GetFullPath($OutputDirectory, $root)
    New-Item -ItemType Directory -Force $output | Out-Null
    $baselineCommit = git rev-parse "$Baseline^{commit}"
    if ($LASTEXITCODE -ne 0) { throw 'Cannot resolve baseline commit.' }
    $source = git show "${baselineCommit}:src/Lakona.Game.LoadTesting/Internal/LoadRunRecorder.cs"
    if ($LASTEXITCODE -ne 0) { throw 'Cannot extract baseline recorder.' }
    $baselineFile = Join-Path $output 'BaselineRecorder.cs'
    $text = "using Lakona.Game.LoadTesting;`n" + ($source -join "`n").Replace(
        'namespace Lakona.Game.LoadTesting.Internal;', 'namespace LoadRecording.Baseline;')
    [System.IO.File]::WriteAllText($baselineFile, $text)
    $baselineCommit | Set-Content (Join-Path $output 'baseline-commit.txt')
    git diff -- src/Lakona.Game.LoadTesting | Set-Content (Join-Path $output 'candidate.patch')
    $snapshot = Join-Path $output 'candidate-source'
    New-Item -ItemType Directory -Force $snapshot | Out-Null
    Copy-Item 'src/Lakona.Game.LoadTesting/Internal/*.cs' $snapshot
    Get-FileHash (Join-Path $snapshot '*.cs'), $baselineFile |
        Select-Object Path, Hash | ConvertTo-Json | Set-Content (Join-Path $output 'source-hashes.json')
    $artifacts = Join-Path $output 'build'
    dotnet build "$PSScriptRoot/LoadRecording.csproj" -c Release --artifacts-path $artifacts `
        "-p:RecorderBaselineFile=$baselineFile" -p:UseSharedCompilation=false
    if ($LASTEXITCODE -ne 0) { throw 'Benchmark build failed.' }
    $previousTiering = $env:DOTNET_TieredCompilation
    try {
        $env:DOTNET_TieredCompilation = '0'
        dotnet (Join-Path $artifacts 'bin/LoadRecording/release/LoadRecording.dll') `
            (Join-Path $output 'results.json') | Tee-Object (Join-Path $output 'run.log')
        if ($LASTEXITCODE -ne 0) { throw 'Benchmark failed.' }
    }
    finally { $env:DOTNET_TieredCompilation = $previousTiering }
}
finally { Pop-Location }
