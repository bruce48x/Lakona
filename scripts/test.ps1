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
    $resultsDirectory = Join-Path $artifactsPath "results/$($project.BaseName)"
    & dotnet test $project.FullName --artifacts-path $artifactsPath -c $Configuration `
        --logger "console;verbosity=minimal" --logger "trx" `
        --results-directory $resultsDirectory `
        --blame-hang-timeout 5m --blame-hang-dump-type none
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}

# Debug uses heap-allocated async state machines. Verify the optimized cache-hit
# allocation contract as well, on the same push/CI path rather than skipping it.
if ($Configuration -eq "Debug") {
    $allocationProject = Join-Path $testsRoot "Lakona.Game.Cluster.Rpc.Tests/Lakona.Game.Cluster.Rpc.Tests.csproj"
    $releaseArtifacts = Join-Path $repositoryRootPath "artifacts/test/release"
    & dotnet test $allocationProject --artifacts-path $releaseArtifacts -c Release `
        --filter "FullyQualifiedName~ClusterClientFactoryTests.Warm_cache_hits_reuse_the_client" `
        --logger "console;verbosity=minimal" --logger "trx" `
        --results-directory (Join-Path $releaseArtifacts "results/cache-allocation") `
        --blame-hang-timeout 5m --blame-hang-dump-type none
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

exit 0
