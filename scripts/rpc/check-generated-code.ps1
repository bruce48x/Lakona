[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",

    [ValidateSet("quiet", "minimal", "normal", "detailed", "diagnostic")]
    [string]$Verbosity = "minimal",

    [switch]$NoRestore,
    [switch]$SkipDiffCheck
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "../..")
$sampleScript = Join-Path $repoRoot "scripts/rpc/sample.ps1"
$samples = @(
    "Rpc.Unity.MemoryPack.Tcp",
    "Rpc.Unity.Json.Websocket",
    "Rpc.Unity.MemoryPack.Kcp",
    "Rpc.Godot.MixedTransport"
)

foreach ($sample in $samples) {
    $arguments = @(
        "-NoProfile",
        "-File", $sampleScript,
        "-Sample", $sample,
        "-Configuration", $Configuration,
        "-Verbosity", $Verbosity,
        "-DisableBuildServer"
    )

    if ($NoRestore) {
        $arguments += "-NoRestore"
    }

    Write-Host "==> checking source-generated sample build for $sample" -ForegroundColor Cyan
    & pwsh @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Source-generated sample build failed for $sample with exit code $LASTEXITCODE"
    }
}

if ($SkipDiffCheck) {
    Write-Host "Source-generated sample builds completed. Skipping git diff check because -SkipDiffCheck was set."
    exit 0
}

$diffOutput = & git -C $repoRoot status --porcelain
if ($LASTEXITCODE -ne 0) {
    throw "git status failed with exit code $LASTEXITCODE"
}

if (-not [string]::IsNullOrWhiteSpace($diffOutput)) {
    Write-Error @"
Source-generated sample checks changed tracked files. Run this command locally and commit the result if the changes are intentional:

  pwsh -NoProfile -File scripts/rpc/check-generated-code.ps1

Changed files:
$diffOutput
"@
    exit 1
}

Write-Host "Source-generated sample builds are up to date."
