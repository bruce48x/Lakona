[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [Parameter(Mandatory = $true)]
    [string]$Rid,

    [Parameter(Mandatory = $true)]
    [string]$PublishRoot,

    [Parameter(Mandatory = $true)]
    [string]$OutputRoot,

    [string]$IconPath = (Join-Path $PSScriptRoot '../../src/Lakona.Hub/Assets/lakona-hub-2048.png')
)

$ErrorActionPreference = 'Stop'
if (-not $IsMacOS) {
    throw 'The macOS DMG must be built on macOS.'
}
if ($Rid -notin @('osx-x64', 'osx-arm64')) {
    throw "Unsupported macOS RID: $Rid"
}

$publishDirectory = (Resolve-Path -LiteralPath $PublishRoot).Path
$iconSource = (Resolve-Path -LiteralPath $IconPath).Path
$hubExecutable = Join-Path $publishDirectory 'Lakona.Hub'
if (-not (Test-Path -LiteralPath $hubExecutable)) {
    throw "The $Rid publish output is incomplete: $publishDirectory"
}

& chmod 0755 $hubExecutable
if ($LASTEXITCODE -ne 0) {
    throw 'Could not mark the Hub entry point executable.'
}

New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null
$outputDirectory = (Resolve-Path -LiteralPath $OutputRoot).Path
$stage = Join-Path $outputDirectory '.dmg-stage'
if (Test-Path -LiteralPath $stage) {
    Remove-Item -LiteralPath $stage -Recurse -Force
}

$app = Join-Path $stage 'Lakona Hub.app'
$macOs = Join-Path $app 'Contents/MacOS'
$resources = Join-Path $app 'Contents/Resources'
New-Item -ItemType Directory -Path $macOs -Force | Out-Null
New-Item -ItemType Directory -Path $resources -Force | Out-Null

$iconSet = Join-Path $stage 'LakonaHub.iconset'
New-Item -ItemType Directory -Path $iconSet -Force | Out-Null
$iconFiles = @(
    @{ Pixels = 16; Name = 'icon_16x16.png' },
    @{ Pixels = 32; Name = 'icon_16x16@2x.png' },
    @{ Pixels = 32; Name = 'icon_32x32.png' },
    @{ Pixels = 64; Name = 'icon_32x32@2x.png' },
    @{ Pixels = 128; Name = 'icon_128x128.png' },
    @{ Pixels = 256; Name = 'icon_128x128@2x.png' },
    @{ Pixels = 256; Name = 'icon_256x256.png' },
    @{ Pixels = 512; Name = 'icon_256x256@2x.png' },
    @{ Pixels = 512; Name = 'icon_512x512.png' },
    @{ Pixels = 1024; Name = 'icon_512x512@2x.png' }
)
foreach ($iconFile in $iconFiles) {
    & sips -z $iconFile.Pixels $iconFile.Pixels $iconSource --out (Join-Path $iconSet $iconFile.Name) | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Could not create macOS icon layer $($iconFile.Name)." }
}
& iconutil -c icns $iconSet -o (Join-Path $resources 'lakona-hub.icns')
if ($LASTEXITCODE -ne 0) { throw 'Could not create the macOS application icon.' }
Remove-Item -LiteralPath $iconSet -Recurse -Force

Get-ChildItem -LiteralPath $publishDirectory -Force | ForEach-Object {
    & ditto $_.FullName (Join-Path $macOs $_.Name)
    if ($LASTEXITCODE -ne 0) { throw "Could not copy $($_.FullName) into the app bundle." }
}

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
  <key>CFBundleIconFile</key><string>lakona-hub.icns</string>
  <key>LSMinimumSystemVersion</key><string>12.0</string>
  <key>NSHighResolutionCapable</key><true/>
</dict>
</plist>
"@ | Set-Content -LiteralPath (Join-Path $app 'Contents/Info.plist') -Encoding utf8NoBOM

New-Item -ItemType SymbolicLink -Path (Join-Path $stage 'Applications') -Target '/Applications' | Out-Null
$packagePath = Join-Path $outputDirectory "lakona-hub-$Version-$Rid.dmg"
try {
    & hdiutil create -volname 'Lakona Hub' -srcfolder $stage -ov -format UDZO $packagePath
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $packagePath)) {
        throw 'hdiutil failed to create the macOS DMG.'
    }
}
finally {
    if (Test-Path -LiteralPath $stage) {
        Remove-Item -LiteralPath $stage -Recurse -Force
    }
}
