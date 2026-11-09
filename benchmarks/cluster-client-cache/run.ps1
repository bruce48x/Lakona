param(
    [string] $Baseline = '4e0f0a68',
    [string] $OutputDirectory = 'artifacts/perf-004/benchmark'
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
Push-Location $root
try {
    $output = [System.IO.Path]::GetFullPath($OutputDirectory, $root)
    New-Item -ItemType Directory -Force $output | Out-Null
    $commit = git rev-parse "$Baseline^{commit}"
    if ($LASTEXITCODE -ne 0) { throw 'Cannot resolve baseline commit.' }
    $source = git show "${commit}:src/Lakona.Game.Server/Cluster/Rpc/Clients/ClusterClientFactory.cs"
    if ($LASTEXITCODE -ne 0) { throw 'Cannot extract baseline factory.' }
    $baselineFile = Join-Path $output 'BaselineClusterClientFactory.cs'
    [System.IO.File]::WriteAllText($baselineFile,
        [regex]::Replace(($source -join "`n"), '\bClusterClientFactory\b', 'BaselineClusterClientFactory'))
    $commit | Set-Content (Join-Path $output 'baseline-commit.txt')
    Copy-Item 'src/Lakona.Game.Server/Cluster/Rpc/Clients/ClusterClientFactory.cs' (Join-Path $output 'CandidateClusterClientFactory.cs')
    Get-FileHash $baselineFile, (Join-Path $output 'CandidateClusterClientFactory.cs') |
        Select-Object Path, Hash | ConvertTo-Json | Set-Content (Join-Path $output 'source-hashes.json')
    $artifacts = Join-Path $output 'build'
    dotnet build "$PSScriptRoot/ClusterClientCache.csproj" -c Release --artifacts-path $artifacts `
        "-p:ClientFactoryBaselineFile=$baselineFile" -p:UseSharedCompilation=false -p:NuGetAudit=false
    if ($LASTEXITCODE -ne 0) { throw 'Benchmark build failed.' }
    $previousTiering = $env:DOTNET_TieredCompilation
    try {
        $env:DOTNET_TieredCompilation = '0'
        dotnet (Join-Path $artifacts 'bin/ClusterClientCache/release/BusinessLogic.Tests.dll') `
            (Join-Path $output 'results.json') | Tee-Object (Join-Path $output 'run.log')
        if ($LASTEXITCODE -ne 0) { throw 'Benchmark failed.' }
    }
    finally { $env:DOTNET_TieredCompilation = $previousTiering }
}
finally { Pop-Location }
