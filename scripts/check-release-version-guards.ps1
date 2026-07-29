param(
    [string] $RepositoryRoot
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
}

$stagedPaths = @(& git -C $RepositoryRoot diff --cached --name-only --diff-filter=ACMRD)
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$hasReleaseRelevantChange = $stagedPaths | Where-Object {
    $path = $_.Replace('\', '/')
    $fileName = [System.IO.Path]::GetFileName($path)
    $path.StartsWith("src/", [System.StringComparison]::Ordinal) -or
        $path.StartsWith("skills/", [System.StringComparison]::Ordinal) -or
        $path.StartsWith("scripts/hub/", [System.StringComparison]::Ordinal) -or
        $path -eq ".github/workflows/publish-hub.yml" -or
        $path -eq ".github/workflows/publish-nuget.yml" -or
        $fileName -in @("Directory.Build.props", "Directory.Build.targets", "global.json") -or
        ($path -match "(^|/)build/" -and ($path.EndsWith(".props", [System.StringComparison]::OrdinalIgnoreCase) -or $path.EndsWith(".targets", [System.StringComparison]::OrdinalIgnoreCase)))
} | Select-Object -First 1

if ($null -eq $hasReleaseRelevantChange) {
    Write-Host "Skipping release version guards: no release-relevant staged changes."
    exit 0
}

Write-Host "Checking release version guards before commit..."
Push-Location $RepositoryRoot
try {
    dotnet test "tests/Lakona.RepositoryGuards.Tests/Lakona.RepositoryGuards.Tests.csproj" --no-restore --filter "PackageVersionGraph|HubVersion|ProjectSystemConsumerVersion"
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}
finally {
    Pop-Location
}
