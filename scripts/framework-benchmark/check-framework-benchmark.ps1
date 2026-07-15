param(
    [switch]$Restore,
    [switch]$RealAdapters
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
if ($LASTEXITCODE -ne 0 -or -not $RealAdapters) {
    exit $LASTEXITCODE
}

& pwsh -NoProfile -File (Join-Path $repositoryRoot "benchmarks/framework/run.ps1") `
    -Workload frontdoor.echo `
    -Output (Join-Path $repositoryRoot "artifacts/framework-benchmark/conformance-smoke")
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

& dotnet test (Join-Path $repositoryRoot "benchmarks/framework/adapters/lakona/FrameworkBenchmark.Lakona.Tests/FrameworkBenchmark.Lakona.Tests.csproj")
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

& npm test --prefix (Join-Path $repositoryRoot "benchmarks/framework/adapters/pinus")
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$coordinator = Join-Path $repositoryRoot "benchmarks/framework/src/FrameworkBenchmark.Coordinator/FrameworkBenchmark.Coordinator.csproj"
& dotnet run --no-build --project $coordinator -- `
    --suite (Join-Path $repositoryRoot "benchmarks/framework/tests/fixtures/echo-conformance.json") `
    --adapter (Join-Path $repositoryRoot "benchmarks/framework/adapters/lakona/adapter.json") `
    --adapter (Join-Path $repositoryRoot "benchmarks/framework/adapters/pinus/adapter.json") `
    --output (Join-Path $repositoryRoot "artifacts/framework-benchmark/conformance-payloads") `
    --no-prepare
exit $LASTEXITCODE
