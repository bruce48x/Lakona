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

foreach ($hookName in @("pre-commit", "pre-push")) {
    $hookPath = Join-Path $repositoryRootPath ".githooks/$hookName"
    if (-not (Test-Path $hookPath -PathType Leaf)) {
        throw "Tracked $hookName hook is missing: $hookPath"
    }
}

$gitConfigExitCode = 1
$gitConfigOutput = @()
$gitConfigAttempts = 10
for ($gitConfigAttempt = 1; $gitConfigAttempt -le $gitConfigAttempts; $gitConfigAttempt++) {
    $gitConfigOutput = @(& git -C $repositoryRootPath config core.hooksPath .githooks 2>&1)
    $gitConfigExitCode = $LASTEXITCODE
    if ($gitConfigExitCode -eq 0) {
        break
    }

    if ($gitConfigAttempt -lt $gitConfigAttempts) {
        Start-Sleep -Milliseconds 250
    }
}

if ($gitConfigExitCode -ne 0) {
    [Console]::Error.WriteLine(($gitConfigOutput -join [Environment]::NewLine))
    exit $gitConfigExitCode
}

Write-Host "Configured core.hooksPath=.githooks for $repositoryRootPath"
