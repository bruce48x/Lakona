param(
    [string] $RepositoryRoot
)

$ErrorActionPreference = "Stop"
$validationCacheLifetime = [TimeSpan]::FromHours(12)

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = (& git rev-parse --show-toplevel).Trim()
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}

function Get-ValidationCacheContext {
    $status = (& git -C $RepositoryRoot status --porcelain --untracked-files=normal | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or -not [string]::IsNullOrWhiteSpace($status)) {
        return $null
    }

    $head = (& git -C $RepositoryRoot rev-parse HEAD | Out-String).Trim()
    $gitDirectory = (& git -C $RepositoryRoot rev-parse --absolute-git-dir | Out-String).Trim()
    $dotnetVersion = (& dotnet --version | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or
        [string]::IsNullOrWhiteSpace($head) -or
        [string]::IsNullOrWhiteSpace($gitDirectory) -or
        [string]::IsNullOrWhiteSpace($dotnetVersion)) {
        return $null
    }

    $environmentIdentity = @(
        $dotnetVersion
        $PSVersionTable.PSVersion.ToString()
        [System.Runtime.InteropServices.RuntimeInformation]::OSDescription
        [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture
    ) -join "`n"
    $identityBytes = [System.Text.Encoding]::UTF8.GetBytes($environmentIdentity)
    $identityHash = [Convert]::ToHexString(
        [System.Security.Cryptography.SHA256]::HashData($identityBytes)).ToLowerInvariant()
    $cacheDirectory = Join-Path $gitDirectory "lakona-validation"

    return [pscustomobject]@{
        CacheDirectory = $cacheDirectory
        Prefix = "$head-$identityHash"
    }
}

function Test-ValidationStamp {
    param(
        [object]$Context,
        [string]$Phase
    )

    if ($null -eq $Context) {
        return $false
    }

    $stampPath = Join-Path $Context.CacheDirectory "$($Context.Prefix)-$Phase.ok"
    if (-not (Test-Path $stampPath -PathType Leaf)) {
        return $false
    }

    return ((Get-Date).ToUniversalTime() - (Get-Item $stampPath).LastWriteTimeUtc) -le
        $validationCacheLifetime
}

function Write-ValidationStamp {
    param(
        [object]$Context,
        [string]$Phase
    )

    if ($null -eq $Context) {
        return
    }

    New-Item -ItemType Directory -Force -Path $Context.CacheDirectory | Out-Null
    $stampPath = Join-Path $Context.CacheDirectory "$($Context.Prefix)-$Phase.ok"
    Set-Content -LiteralPath $stampPath -Value ([DateTimeOffset]::UtcNow.ToString("O")) -Encoding UTF8
}

$cacheContext = Get-ValidationCacheContext

$testScript = Join-Path $RepositoryRoot "scripts/test.ps1"
if (-not (Test-Path $testScript -PathType Leaf)) {
    throw "Repository test script is missing: $testScript"
}

if (Test-ValidationStamp $cacheContext "tests") {
    Write-Host "Reusing repository test result for this clean HEAD and toolchain."
} else {
    Write-Host "Running repository tests before push..."
    & pwsh -NoProfile -File $testScript -RepositoryRoot $RepositoryRoot
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }

    Write-ValidationStamp $cacheContext "tests"
}

$e2eScript = Join-Path $RepositoryRoot ".agents/skills/lakona-e2e-testing/scripts/run-e2e.ps1"
if (-not (Test-Path $e2eScript -PathType Leaf)) {
    throw "Local package E2E script is missing: $e2eScript"
}

if (Test-ValidationStamp $cacheContext "local-feed-e2e") {
    Write-Host "Reusing local package E2E result for this clean HEAD and toolchain."
    exit 0
}

Write-Host "Running required local package E2E before push..."
& pwsh -NoProfile -File $e2eScript -Feed LocalFeed -Port 30000 -FindAvailablePort
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Write-ValidationStamp $cacheContext "local-feed-e2e"
exit 0
