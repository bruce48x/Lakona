param(
    [string] $RepositoryRoot,
    [ValidateSet("Debug", "Release")]
    [string] $Configuration = "Debug"
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = (& git rev-parse --show-toplevel).Trim()
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}

$repositoryRootPath = [System.IO.Path]::GetFullPath($RepositoryRoot)
$solution = Join-Path $repositoryRootPath "tests/Tests.slnx"
$artifactsPath = Join-Path $repositoryRootPath "artifacts/test/$($Configuration.ToLowerInvariant())"

if (-not (Test-Path $solution -PathType Leaf)) {
    throw "Repository test solution is missing: $solution"
}

Write-Host "Running repository tests with isolated artifacts..."
& dotnet test $solution --artifacts-path $artifactsPath -c $Configuration
exit $LASTEXITCODE
