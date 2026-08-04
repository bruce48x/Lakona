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
$testsRoot = Join-Path $repositoryRootPath "tests"
$artifactsPath = Join-Path $repositoryRootPath "artifacts/test/$($Configuration.ToLowerInvariant())"

if (-not (Test-Path $testsRoot -PathType Container)) {
    throw "Repository tests directory is missing: $testsRoot"
}

$projects = @(
    Get-ChildItem -LiteralPath $testsRoot -Recurse -Filter "*.csproj" -File |
        Sort-Object FullName
)
if ($projects.Count -eq 0) {
    throw "Repository contains no test projects: $testsRoot"
}

Write-Host "Running repository test projects sequentially with isolated artifacts..."
$index = 0
foreach ($project in $projects) {
    $index++
    Write-Host "[$index/$($projects.Count)] $($project.FullName)"
    & dotnet test $project.FullName --artifacts-path $artifactsPath -c $Configuration
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}

exit 0
