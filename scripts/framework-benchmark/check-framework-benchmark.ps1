param(
    [switch]$Restore
)

$ErrorActionPreference = "Stop"
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "../..")).Path
$solution = Join-Path $repositoryRoot "benchmarks/framework/FrameworkBenchmark.slnx"

& pwsh -NoProfile -File (Join-Path $repositoryRoot "scripts/rpc/check-docs-consistency.ps1")
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$arguments = @("test", $solution)
if (-not $Restore) {
    $arguments += "--no-restore"
}

& dotnet @arguments
exit $LASTEXITCODE
