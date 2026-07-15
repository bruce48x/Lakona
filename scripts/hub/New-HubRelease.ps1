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
    [string]$OutputRoot,

    [string]$PreviousRoot
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$deploymentModel = 'native-aot-v1'

$portablePlatforms = @(
    @{ Rid = 'win-x64'; PackageRoot = 'Lakona Hub'; ExecutablePath = 'Lakona.Hub.exe'; SdkExecutable = 'dotnet/dotnet.exe' },
    @{ Rid = 'osx-x64'; PackageRoot = 'Lakona Hub.app'; ExecutablePath = 'Contents/MacOS/Lakona.Hub'; SdkExecutable = 'Contents/Resources/dotnet/dotnet' },
    @{ Rid = 'osx-arm64'; PackageRoot = 'Lakona Hub.app'; ExecutablePath = 'Contents/MacOS/Lakona.Hub'; SdkExecutable = 'Contents/Resources/dotnet/dotnet' }
)

$linuxPackages = @(
    @{ Platform = 'linux-x64-deb'; SourceName = "lakona-hub_${Version}_amd64.deb" },
    @{ Platform = 'linux-x64-rpm'; SourceName = "lakona-hub-${Version}-1.x86_64.rpm" }
)

function Get-Sha256([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Get-ArchivePath([string]$Root, [string]$Path) {
    return [IO.Path]::GetRelativePath($Root, $Path).Replace('\', '/')
}

function Write-JsonFile([string]$Path, [object]$Value) {
    $Value | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $Path -Encoding utf8NoBOM
}

function Copy-DirectoryContents([string]$Source, [string]$Destination) {
    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    Get-ChildItem -LiteralPath $Source -Force | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $Destination -Recurse -Force
    }
}

function New-PackageTree([hashtable]$Platform, [string]$Source, [string]$Stage) {
    $packageRoot = Join-Path $Stage $Platform.PackageRoot
    New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null
    if ($Platform.Rid.StartsWith('osx-', [StringComparison]::Ordinal)) {
        $macOs = Join-Path $packageRoot 'Contents/MacOS'
        $resources = Join-Path $packageRoot 'Contents/Resources'
        New-Item -ItemType Directory -Path $macOs -Force | Out-Null
        New-Item -ItemType Directory -Path $resources -Force | Out-Null
        Get-ChildItem -LiteralPath $Source -Force | Where-Object Name -ne 'dotnet' | ForEach-Object {
            Copy-Item -LiteralPath $_.FullName -Destination $macOs -Recurse -Force
        }
        Copy-Item -LiteralPath (Join-Path $Source 'dotnet') -Destination $resources -Recurse -Force
        @"
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>Lakona Hub</string>
  <key>CFBundleDisplayName</key><string>Lakona Hub</string>
  <key>CFBundleIdentifier</key><string>dev.lakona.hub</string>
  <key>CFBundleVersion</key><string>$Version</string>
  <key>CFBundleShortVersionString</key><string>$Version</string>
  <key>CFBundleExecutable</key><string>Lakona.Hub</string>
  <key>LSMinimumSystemVersion</key><string>12.0</string>
  <key>NSHighResolutionCapable</key><true/>
</dict>
</plist>
"@ | Set-Content -LiteralPath (Join-Path $packageRoot 'Contents/Info.plist') -Encoding utf8NoBOM
    }
    else {
        Copy-DirectoryContents $Source $packageRoot
    }

    return $packageRoot
}

function New-PackageManifest([hashtable]$Platform, [string]$PackageRoot) {
    $files = @(Get-ChildItem -LiteralPath $PackageRoot -File -Recurse -Force |
        Where-Object Name -ne 'hub-package.json' |
        Sort-Object FullName |
        ForEach-Object {
            [ordered]@{
                path = Get-ArchivePath $PackageRoot $_.FullName
                sha256 = Get-Sha256 $_.FullName
                size = $_.Length
            }
        })
    $manifest = [ordered]@{
        schemaVersion = 1
        version = $Version
        files = $files
        executableFiles = @($Platform.ExecutablePath, $Platform.SdkExecutable)
    }
    Write-JsonFile (Join-Path $PackageRoot 'hub-package.json') $manifest
    return $manifest
}

function New-Zip([string]$Source, [string]$Destination) {
    if (Test-Path -LiteralPath $Destination) {
        Remove-Item -LiteralPath $Destination -Force
    }
    [IO.Compression.ZipFile]::CreateFromDirectory(
        $Source,
        $Destination,
        [IO.Compression.CompressionLevel]::Optimal,
        $false)
}

function Read-PreviousManifest {
    if ([string]::IsNullOrWhiteSpace($PreviousRoot)) {
        return $null
    }
    $path = Join-Path $PreviousRoot 'lakona-hub-manifest.json'
    if (-not (Test-Path -LiteralPath $path)) {
        return $null
    }
    return Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
}

function New-Delta(
    [hashtable]$Platform,
    [string]$PackageRoot,
    [object]$PackageManifest,
    [object]$PreviousManifest,
    [string]$StageRoot
) {
    if ($null -eq $PreviousManifest) {
        return $null
    }
    if ($PreviousManifest.deploymentModel -ne $deploymentModel) {
        return $null
    }
    $previousPlatform = $PreviousManifest.platforms.($Platform.Rid)
    if ($null -eq $previousPlatform) {
        return $null
    }
    $previousArchive = Join-Path $PreviousRoot $previousPlatform.full.assetName
    if (-not (Test-Path -LiteralPath $previousArchive)) {
        return $null
    }

    $oldExtract = Join-Path $StageRoot ('old-' + $Platform.Rid)
    [IO.Compression.ZipFile]::ExtractToDirectory($previousArchive, $oldExtract)
    $oldRoot = Join-Path $oldExtract $previousPlatform.packageRoot
    $oldPackageManifest = Get-Content -LiteralPath (Join-Path $oldRoot 'hub-package.json') -Raw | ConvertFrom-Json
    $oldFiles = @{}
    foreach ($file in $oldPackageManifest.files) { $oldFiles[$file.path] = $file }
    $newFiles = @{}
    foreach ($file in $PackageManifest.files) { $newFiles[$file.path] = $file }

    $deltaRoot = Join-Path $StageRoot ('delta-' + $Platform.Rid)
    New-Item -ItemType Directory -Path $deltaRoot -Force | Out-Null
    foreach ($file in $PackageManifest.files) {
        if (-not $oldFiles.ContainsKey($file.path) -or $oldFiles[$file.path].sha256 -ne $file.sha256) {
            $source = Join-Path $PackageRoot $file.path
            $destination = Join-Path $deltaRoot $file.path
            New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
            Copy-Item -LiteralPath $source -Destination $destination -Force
        }
    }
    Copy-Item -LiteralPath (Join-Path $PackageRoot 'hub-package.json') -Destination $deltaRoot -Force
    $deleted = @($oldPackageManifest.files |
        Where-Object { -not $newFiles.ContainsKey($_.path) } |
        ForEach-Object path |
        Sort-Object)
    Write-JsonFile (Join-Path $deltaRoot 'hub-delta.json') ([ordered]@{
        schemaVersion = 1
        fromVersion = $PreviousManifest.version
        toVersion = $Version
        deletedFiles = $deleted
    })

    $assetName = "lakona-hub-$($PreviousManifest.version)-to-$Version-$($Platform.Rid).delta.zip"
    $assetPath = Join-Path $OutputRoot $assetName
    New-Zip $deltaRoot $assetPath
    return [ordered]@{
        fromVersion = $PreviousManifest.version
        assetName = $assetName
        sha256 = Get-Sha256 $assetPath
        size = (Get-Item -LiteralPath $assetPath).Length
    }
}

New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null
$stageRoot = Join-Path $OutputRoot '.stage'
if (Test-Path -LiteralPath $stageRoot) { Remove-Item -LiteralPath $stageRoot -Recurse -Force }
New-Item -ItemType Directory -Path $stageRoot | Out-Null
$previousManifest = Read-PreviousManifest
$releasePlatforms = [ordered]@{}

try {
    foreach ($platform in $portablePlatforms) {
        $source = Join-Path $PublishRoot ("hub-" + $platform.Rid)
        if (-not (Test-Path -LiteralPath $source)) {
            throw "Missing publish artifact: $source"
        }
        $platformStage = Join-Path $stageRoot $platform.Rid
        New-Item -ItemType Directory -Path $platformStage | Out-Null
        $packageRoot = New-PackageTree $platform $source $platformStage
        $packageManifest = New-PackageManifest $platform $packageRoot
        $fullAssetName = "lakona-hub-$Version-$($platform.Rid).zip"
        $fullAssetPath = Join-Path $OutputRoot $fullAssetName
        New-Zip $platformStage $fullAssetPath
        $delta = New-Delta $platform $packageRoot $packageManifest $previousManifest $stageRoot
        $releasePlatforms[$platform.Rid] = [ordered]@{
            packageRoot = $platform.PackageRoot
            executablePath = $platform.ExecutablePath
            full = [ordered]@{
                assetName = $fullAssetName
                sha256 = Get-Sha256 $fullAssetPath
                size = (Get-Item -LiteralPath $fullAssetPath).Length
            }
            deltas = @($delta | Where-Object { $null -ne $_ })
        }
    }

    $linuxPackageRoot = Join-Path $PublishRoot 'hub-linux-packages'
    foreach ($package in $linuxPackages) {
        $source = Join-Path $linuxPackageRoot $package.SourceName
        if (-not (Test-Path -LiteralPath $source)) {
            throw "Missing Linux package artifact: $source"
        }

        $destination = Join-Path $OutputRoot $package.SourceName
        Copy-Item -LiteralPath $source -Destination $destination -Force
        $releasePlatforms[$package.Platform] = [ordered]@{
            packageRoot = '/usr/lib/lakona-hub'
            executablePath = 'Lakona.Hub'
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
        deploymentModel = $deploymentModel
        version = $Version
        tag = $Tag
        publishedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        repository = $Repository
        platforms = $releasePlatforms
    })
}
finally {
    if (Test-Path -LiteralPath $stageRoot) { Remove-Item -LiteralPath $stageRoot -Recurse -Force }
}
