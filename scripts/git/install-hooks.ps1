param(
    [string] $RepositoryRoot
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
}

$repositoryRootPath = [System.IO.Path]::GetFullPath($RepositoryRoot)
if (-not (Test-Path (Join-Path $repositoryRootPath ".git"))) {
    throw "Not a Git repository: $repositoryRootPath"
}

$hookPath = Join-Path $repositoryRootPath ".githooks/pre-commit"
if (-not (Test-Path $hookPath -PathType Leaf)) {
    throw "Tracked pre-commit hook is missing: $hookPath"
}

& git -C $repositoryRootPath config core.hooksPath .githooks
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Write-Host "Configured core.hooksPath=.githooks for $repositoryRootPath"
