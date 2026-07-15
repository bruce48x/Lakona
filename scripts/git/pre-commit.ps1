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

$stagedPaths = @(& git -C $RepositoryRoot diff --cached --name-only --diff-filter=ACMRD)
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$hasReleaseRelevantChange = $stagedPaths | Where-Object {
    $path = $_.Replace('\', '/')
    $fileName = [System.IO.Path]::GetFileName($path)
    $path.StartsWith("src/", [System.StringComparison]::Ordinal) -or
        $path.StartsWith("scripts/hub/", [System.StringComparison]::Ordinal) -or
        $path -eq ".github/workflows/publish-hub.yml" -or
        $fileName -in @("Directory.Build.props", "Directory.Build.targets", "global.json") -or
        ($path -match "(^|/)build/" -and ($path.EndsWith(".props", [System.StringComparison]::OrdinalIgnoreCase) -or $path.EndsWith(".targets", [System.StringComparison]::OrdinalIgnoreCase)))
} | Select-Object -First 1

if ($null -eq $hasReleaseRelevantChange) {
    exit 0
}

Write-Host "Checking release version guards before commit..."
$guardScript = Join-Path $RepositoryRoot "scripts/check-release-version-guards.ps1"
& pwsh -NoProfile -File $guardScript
exit $LASTEXITCODE
