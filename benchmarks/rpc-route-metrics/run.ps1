param(
    [string] $Baseline = 'ec121102',
    [string] $OutputDirectory = 'artifacts/ops-002/benchmark'
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
Push-Location $root
try {
    $output = [System.IO.Path]::GetFullPath($OutputDirectory, $root)
    New-Item -ItemType Directory -Force $output | Out-Null
    $commit = git rev-parse "$Baseline^{commit}"
    if ($LASTEXITCODE -ne 0) { throw 'Cannot resolve baseline.' }
    $commit | Set-Content (Join-Path $output 'baseline-commit.txt')
    foreach ($path in @('Dispatching/RpcSession.cs', 'Observability/RpcServerTelemetry.cs')) {
        $source = git show "${commit}:src/Lakona.Rpc.Server/$path"
        if ($LASTEXITCODE -ne 0) { throw "Cannot extract $path." }
        $name = Split-Path $path -Leaf
        [System.IO.File]::WriteAllText((Join-Path $output $name), ($source -join "`n"))
        Copy-Item "src/Lakona.Rpc.Server/$path" (Join-Path $output "Candidate-$name.txt")
    }
    $escaped = [System.Security.SecurityElement]::Escape($output)
    $targets = @"
<Project>
  <Target Name="UseBaselineRpc" BeforeTargets="CoreCompile" Condition="'`$(MSBuildProjectName)' == 'Lakona.Rpc.Server'">
    <ItemGroup>
      <Compile Remove="@(Compile)" Condition="'%(Compile.Filename)' == 'RpcSession' Or '%(Compile.Filename)' == 'RpcServerTelemetry'" />
      <Compile Include="$escaped/RpcSession.cs" />
      <Compile Include="$escaped/RpcServerTelemetry.cs" />
    </ItemGroup>
  </Target>
</Project>
"@
    $targetFile = Join-Path $output 'baseline.targets'
    $targets | Set-Content $targetFile
    Copy-Item "$PSScriptRoot/Program.cs" (Join-Path $output 'Program.cs.txt')
    Get-FileHash (Join-Path $output '*.cs'), (Join-Path $output '*.cs.txt') |
        Select-Object Path, Hash | ConvertTo-Json | Set-Content (Join-Path $output 'source-hashes.json')
    foreach ($variant in @('baseline', 'candidate')) {
        $extra = @()
        if ($variant -eq 'baseline') {
            $extra = @('-p:UseBaseline=true', "-p:CustomBeforeMicrosoftCommonTargets=$targetFile")
        }
        dotnet build "$PSScriptRoot/RpcRouteMetrics.csproj" -c Release --artifacts-path (Join-Path $output $variant) `
            -p:UseSharedCompilation=false -p:NuGetAudit=false @extra
        if ($LASTEXITCODE -ne 0) { throw "$variant build failed." }
    }
    $previousTiering = $env:DOTNET_TieredCompilation
    try {
        $env:DOTNET_TieredCompilation = '0'
        for ($pair = 0; $pair -lt 3; $pair++) {
            $order = if ($pair % 2 -eq 0) { @('baseline', 'candidate') } else { @('candidate', 'baseline') }
            foreach ($variant in $order) {
                dotnet (Join-Path $output "$variant/bin/RpcRouteMetrics/release/Lakona.Rpc.Tests.dll") `
                    (Join-Path $output "$variant-$pair.json") | Tee-Object (Join-Path $output "$variant-$pair.log")
                if ($LASTEXITCODE -ne 0) { throw "$variant benchmark failed." }
            }
        }
    }
    finally { $env:DOTNET_TieredCompilation = $previousTiering }
}
finally { Pop-Location }
