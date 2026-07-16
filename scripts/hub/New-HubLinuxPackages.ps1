[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [Parameter(Mandatory = $true)]
    [string]$PublishRoot,

    [Parameter(Mandatory = $true)]
    [string]$OutputRoot,

    [string]$NfpmPath = 'nfpm'
)

$ErrorActionPreference = 'Stop'
$publishDirectory = (Resolve-Path -LiteralPath $PublishRoot).Path
$hubExecutable = Join-Path $publishDirectory 'Lakona.Hub'
if (-not (Test-Path -LiteralPath $hubExecutable)) {
    throw "The linux-x64 publish output is incomplete: $publishDirectory"
}

if (-not $IsLinux) {
    throw 'Linux system packages must be built on Linux so payload permissions can be preserved.'
}

& chmod 0755 $hubExecutable
if ($LASTEXITCODE -ne 0) {
    throw 'Could not mark the Hub entry point executable.'
}

New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null
$outputDirectory = (Resolve-Path -LiteralPath $OutputRoot).Path
$env:HUB_VERSION = $Version
$env:HUB_PUBLISH_ROOT = $publishDirectory
$config = Join-Path $PSScriptRoot 'linux/nfpm.yaml'

$targets = @(
    @{ Packager = 'deb'; Name = "lakona-hub-$Version-linux-x64.deb" },
    @{ Packager = 'rpm'; Name = "lakona-hub-$Version-linux-x64.rpm" }
)
foreach ($target in $targets) {
    $path = Join-Path $outputDirectory $target.Name
    & $NfpmPath package --config $config --packager $target.Packager --target $path
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $path)) {
        throw "nFPM failed to create $($target.Packager) package."
    }
}
