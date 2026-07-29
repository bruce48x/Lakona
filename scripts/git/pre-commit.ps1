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

$guardScript = Join-Path $RepositoryRoot "scripts/check-release-version-guards.ps1"
& pwsh -NoProfile -File $guardScript -RepositoryRoot $RepositoryRoot
exit $LASTEXITCODE
