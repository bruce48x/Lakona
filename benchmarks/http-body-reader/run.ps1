param(
    [string] $Baseline = 'f2f7ead2',
    [string] $OutputDirectory = 'artifacts/perf-005/benchmark'
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
Push-Location $root
try {
    $output = [System.IO.Path]::GetFullPath($OutputDirectory, $root)
    New-Item -ItemType Directory -Force $output | Out-Null
    $commit = git rev-parse "$Baseline^{commit}"
    if ($LASTEXITCODE -ne 0) { throw 'Cannot resolve baseline.' }
    $path = 'src/Lakona.Game.Server/Management/LakonaHttpHosting.cs'
    $old = git show "${commit}:$path"
    if ($LASTEXITCODE -ne 0) { throw 'Cannot read baseline.' }
    $sources = @{ Baseline = ($old -join "`n"); Candidate = (Get-Content -Raw $path) }
    foreach ($name in @('Baseline', 'Candidate')) {
        $source = $sources[$name]
        [System.IO.File]::WriteAllText((Join-Path $output "$($name)Hosting.cs.txt"), $source)
        $match = [regex]::Match($source, '(?s)    (?:private|internal) static async Task<byte\[\]\?> ReadBodyAsync\(.*?(?=    private static LakonaHttpCall CreateCall\()')
        if (!$match.Success) { throw "Cannot locate $name reader method." }
        $method = $match.Value -replace 'private static async', 'internal static async'
        $wrapper = "using System.Buffers;`nusing Microsoft.AspNetCore.Http;`nusing Microsoft.AspNetCore.Http.Features;`ninternal static class $($name)Reader`n{`n$method`n}"
        [System.IO.File]::WriteAllText((Join-Path $output "$($name)Reader.cs"), $wrapper)
    }
    $commit | Set-Content (Join-Path $output 'baseline-commit.txt')
    Get-FileHash (Join-Path $output '*Reader.cs') | Select-Object Path, Hash |
        ConvertTo-Json | Set-Content (Join-Path $output 'source-hashes.json')
    $artifacts = Join-Path $output 'build'
    dotnet build "$PSScriptRoot/HttpBodyReader.csproj" -c Release --artifacts-path $artifacts `
        "-p:ReaderSourceDirectory=$output" -p:UseSharedCompilation=false -p:NuGetAudit=false
    if ($LASTEXITCODE -ne 0) { throw 'Benchmark build failed.' }
    $previousTiering = $env:DOTNET_TieredCompilation
    try {
        $env:DOTNET_TieredCompilation = '0'
        dotnet (Join-Path $artifacts 'bin/HttpBodyReader/release/HttpBodyReader.dll') `
            (Join-Path $output 'results.json') | Tee-Object (Join-Path $output 'run.log')
        if ($LASTEXITCODE -ne 0) { throw 'Benchmark failed.' }
    }
    finally { $env:DOTNET_TieredCompilation = $previousTiering }
}
finally { Pop-Location }
