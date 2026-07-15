param(
    [ValidateSet("smoke", "v1")]
    [string]$Suite = "smoke",

    [ValidateSet("all", "lakona", "pinus")]
    [string]$Framework = "all",

    [string]$Output,

    [switch]$NoPrepare
)

$ErrorActionPreference = "Stop"
$benchmarkRoot = $PSScriptRoot
$repositoryRoot = (Resolve-Path (Join-Path $benchmarkRoot "../..")).Path
$solution = Join-Path $benchmarkRoot "FrameworkBenchmark.slnx"
$suitePath = Join-Path $benchmarkRoot "suites/$Suite.json"

if ([string]::IsNullOrWhiteSpace($Output)) {
    $Output = Join-Path $repositoryRoot "artifacts/framework-benchmark"
}

$frameworks = if ($Framework -eq "all") { @("lakona", "pinus") } else { @($Framework) }
$manifests = @()
foreach ($name in $frameworks) {
    $manifest = Join-Path $benchmarkRoot "adapters/$name/adapter.json"
    if (-not (Test-Path -LiteralPath $manifest)) {
        throw "Framework benchmark adapter '$name' is not implemented at '$manifest'."
    }

    $manifests += $manifest
}

if (-not $NoPrepare) {
    & dotnet build $solution
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}

$arguments = @(
    "run",
    "--no-build",
    "--project",
    (Join-Path $benchmarkRoot "src/FrameworkBenchmark.Coordinator/FrameworkBenchmark.Coordinator.csproj"),
    "--",
    "--suite",
    $suitePath,
    "--output",
    $Output
)
foreach ($manifest in $manifests) {
    $arguments += @("--adapter", $manifest)
}

& dotnet @arguments
exit $LASTEXITCODE
