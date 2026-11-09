param(
    [string] $Baseline = '99ea5f2d',
    [string] $OutputDirectory = 'artifacts/perf-006/benchmark',
    [ValidateSet('all', 'collection', 'operations')]
    [string] $Mode = 'all'
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
Push-Location $root
try {
    $output = [System.IO.Path]::GetFullPath($OutputDirectory, $root)
    New-Item -ItemType Directory -Force $output | Out-Null
    $commit = git rev-parse "$Baseline^{commit}"
    if ($LASTEXITCODE -ne 0) { throw 'Cannot resolve baseline.' }
    $files = @{ Registry = 'InMemoryGameSessionRegistry'; Telemetry = 'GameSessionPopulationTelemetry' }
    foreach ($key in $files.Keys) {
        $type = $files[$key]
        $path = "src/Lakona.Game.Server/Sessions/$type.cs"
        $source = git show "${commit}:$path"
        if ($LASTEXITCODE -ne 0) { throw "Cannot extract $type." }
        [System.IO.File]::WriteAllText((Join-Path $output "Baseline$key.cs"),
            [regex]::Replace(($source -join "`n"), "\b$type\b", "Baseline$key"))
        Copy-Item $path (Join-Path $output "Candidate$key.cs.txt")
    }
    $commit | Set-Content (Join-Path $output 'baseline-commit.txt')
    # Isolate collection interference on one registry with identical business
    # operations, session identities and object layout for both readers.
    $oldRegistry = Get-Content -Raw (Join-Path $output 'BaselineRegistry.cs')
    $scan = [regex]::Match($oldRegistry, '(?s)    public GameSessionDiagnosticsSnapshot GetDiagnosticsSnapshot\(\).*?(?=    public ValueTask<IReadOnlyList<GameSessionExpiration>>)')
    if (!$scan.Success) { throw 'Cannot locate baseline diagnostics method.' }
    $oldScan = $scan.Value.Replace('public GameSessionDiagnosticsSnapshot GetDiagnosticsSnapshot()', 'private GameSessionDiagnosticsSnapshot GetBaselineDiagnosticsSnapshot()')
    $probe = Get-Content -Raw (Join-Path $output 'CandidateRegistry.cs.txt')
    $probe = [regex]::Replace($probe, '\bInMemoryGameSessionRegistry\b', 'ProbeRegistry')
    $probe = $probe.Replace('public GameSessionDiagnosticsSnapshot GetDiagnosticsSnapshot()', 'private GameSessionDiagnosticsSnapshot GetCurrentDiagnosticsSnapshot()')
    $dispatch = "    public bool UseBaselineDiagnostics { get; set; }`n    public GameSessionDiagnosticsSnapshot GetDiagnosticsSnapshot() => UseBaselineDiagnostics ? GetBaselineDiagnosticsSnapshot() : GetCurrentDiagnosticsSnapshot();`n"
    $closing = $probe.LastIndexOf('}')
    $probe = $probe.Insert($closing, $dispatch + $oldScan)
    [System.IO.File]::WriteAllText((Join-Path $output 'ProbeRegistry.cs'), $probe)
    Copy-Item "$PSScriptRoot/Program.cs" (Join-Path $output 'Program.cs.txt')
    Get-FileHash (Join-Path $output '*.cs'), (Join-Path $output '*.cs.txt') |
        Select-Object Path, Hash | ConvertTo-Json | Set-Content (Join-Path $output 'source-hashes.json')
    $artifacts = Join-Path $output 'build'
    dotnet build "$PSScriptRoot/SessionPopulation.csproj" -c Release --artifacts-path $artifacts `
        "-p:PopulationBaselineDirectory=$output" -p:UseSharedCompilation=false -p:NuGetAudit=false
    if ($LASTEXITCODE -ne 0) { throw 'Benchmark build failed.' }
    $previousTiering = $env:DOTNET_TieredCompilation
    try {
        $env:DOTNET_TieredCompilation = '0'
        dotnet (Join-Path $artifacts 'bin/SessionPopulation/release/BusinessLogic.Tests.dll') `
            (Join-Path $output 'results.json') $Mode | Tee-Object (Join-Path $output 'run.log')
        if ($LASTEXITCODE -ne 0) { throw 'Benchmark failed.' }
    }
    finally { $env:DOTNET_TieredCompilation = $previousTiering }
}
finally { Pop-Location }
