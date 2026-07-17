param(
    [string] $RepositoryRoot
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = (& git rev-parse --show-toplevel).Trim()
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}

$testScript = Join-Path $RepositoryRoot "scripts/test.ps1"
if (-not (Test-Path $testScript -PathType Leaf)) {
    throw "Repository test script is missing: $testScript"
}

Write-Host "Running repository tests before push..."
& pwsh -NoProfile -File $testScript -RepositoryRoot $RepositoryRoot
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$e2eScript = Join-Path $RepositoryRoot ".agents/skills/lakona-e2e-testing/scripts/run-e2e.ps1"
if (-not (Test-Path $e2eScript -PathType Leaf)) {
    throw "Local package E2E script is missing: $e2eScript"
}

Write-Host "Running required local package E2E before push..."
& pwsh -NoProfile -File $e2eScript -Feed LocalFeed
exit $LASTEXITCODE
