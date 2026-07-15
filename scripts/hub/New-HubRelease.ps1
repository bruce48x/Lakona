[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [Parameter(Mandatory = $true)]
    [string]$Tag,

    [Parameter(Mandatory = $true)]
    [string]$Repository,

    [Parameter(Mandatory = $true)]
    [string]$PublishRoot,

    [Parameter(Mandatory = $true)]
    [string]$OutputRoot
)

$ErrorActionPreference = 'Stop'

$packages = @(
    @{
        Platform = 'win-x64'
        ArtifactRoot = 'hub-win-x64-package'
        SourceName = "lakona-hub-$Version-win-x64.msi"
        PackageRoot = 'C:/Program Files/Lakona Hub'
        ExecutablePath = 'Lakona.Hub.exe'
    },
    @{
        Platform = 'osx-x64'
        ArtifactRoot = 'hub-osx-x64-package'
        SourceName = "lakona-hub-$Version-osx-x64.dmg"
        PackageRoot = '/Applications/Lakona Hub.app'
        ExecutablePath = 'Contents/MacOS/Lakona.Hub'
    },
    @{
        Platform = 'osx-arm64'
        ArtifactRoot = 'hub-osx-arm64-package'
        SourceName = "lakona-hub-$Version-osx-arm64.dmg"
        PackageRoot = '/Applications/Lakona Hub.app'
        ExecutablePath = 'Contents/MacOS/Lakona.Hub'
    },
    @{
        Platform = 'linux-x64-deb'
        ArtifactRoot = 'hub-linux-packages'
        SourceName = "lakona-hub-$Version-linux-x64.deb"
        PackageRoot = '/usr/lib/lakona-hub'
        ExecutablePath = 'Lakona.Hub'
    },
    @{
        Platform = 'linux-x64-rpm'
        ArtifactRoot = 'hub-linux-packages'
        SourceName = "lakona-hub-$Version-linux-x64.rpm"
        PackageRoot = '/usr/lib/lakona-hub'
        ExecutablePath = 'Lakona.Hub'
    }
)

function Get-Sha256([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Write-JsonFile([string]$Path, [object]$Value) {
    $Value | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $Path -Encoding utf8NoBOM
}

New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null
$releasePlatforms = [ordered]@{}
foreach ($package in $packages) {
    $source = Join-Path (Join-Path $PublishRoot $package.ArtifactRoot) $package.SourceName
    if (-not (Test-Path -LiteralPath $source)) {
        throw "Missing release package: $source"
    }

    $destination = Join-Path $OutputRoot $package.SourceName
    Copy-Item -LiteralPath $source -Destination $destination -Force
    $releasePlatforms[$package.Platform] = [ordered]@{
        packageRoot = $package.PackageRoot
        executablePath = $package.ExecutablePath
        full = [ordered]@{
            assetName = $package.SourceName
            sha256 = Get-Sha256 $destination
            size = (Get-Item -LiteralPath $destination).Length
        }
        deltas = @()
    }
}

Write-JsonFile (Join-Path $OutputRoot 'lakona-hub-manifest.json') ([ordered]@{
    schemaVersion = 1
    version = $Version
    tag = $Tag
    publishedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    repository = $Repository
    platforms = $releasePlatforms
})
