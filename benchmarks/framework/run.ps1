param(
    [ValidateSet("smoke", "v1")]
    [string]$Suite = "smoke",

    [ValidateSet("all", "lakona", "pinus")]
    [string]$Framework = "all",

    [ValidateSet("all", "frontdoor.echo", "cluster.direct", "cluster.routed")]
    [string]$Workload = "all",

    [string]$Output,

    [switch]$NoPrepare
)

$ErrorActionPreference = "Stop"
$benchmarkRoot = $PSScriptRoot
$repositoryRoot = (Resolve-Path (Join-Path $benchmarkRoot "../..")).Path
$solution = Join-Path $benchmarkRoot "FrameworkBenchmark.slnx"
$suitePath = Join-Path $benchmarkRoot "suites/$Suite.json"

function Assert-Command([string]$Name, [string]$Guidance) {
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required command '$Name' was not found. $Guidance"
    }
}

if ($PSVersionTable.PSVersion.Major -lt 7) {
    throw "PowerShell 7 or newer is required. Rerun this command with pwsh."
}

Assert-Command "dotnet" "Install the .NET 10 SDK, then rerun the same command."
if ($Framework -in @("all", "pinus")) {
    Assert-Command "node" "Install Node.js, then rerun the same command."
    Assert-Command "npm" "Install npm with Node.js, then rerun the same command."
}

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
else {
    $requiredOutputs = @(
        (Join-Path $benchmarkRoot "src/FrameworkBenchmark.Coordinator/bin/Debug/net10.0/FrameworkBenchmark.Coordinator.dll")
    )
    if ($Framework -in @("all", "lakona")) {
        $requiredOutputs += @(
            (Join-Path $benchmarkRoot "adapters/lakona/FrameworkBenchmark.Lakona.Server/bin/Release/net10.0/FrameworkBenchmark.Lakona.Server.dll"),
            (Join-Path $benchmarkRoot "adapters/lakona/FrameworkBenchmark.Lakona.Driver/bin/Release/net10.0/FrameworkBenchmark.Lakona.Driver.dll")
        )
    }

    if ($Framework -in @("all", "pinus")) {
        $requiredOutputs += (Join-Path $benchmarkRoot "adapters/pinus/dist/driver.js")
    }

    $missingOutputs = @($requiredOutputs | Where-Object { -not (Test-Path -LiteralPath $_) })
    if ($missingOutputs.Count -gt 0) {
        throw "-NoPrepare requires existing build output. Rerun without -NoPrepare first. Missing: $($missingOutputs -join ', ')"
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

if ($Framework -ne "all") {
    $arguments += @("--framework", $Framework)
}

if ($Workload -ne "all") {
    $arguments += @("--workload", $Workload)
}

if ($NoPrepare) {
    $arguments += "--no-prepare"
}

& dotnet @arguments
exit $LASTEXITCODE
