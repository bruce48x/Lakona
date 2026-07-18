[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [Parameter(Mandatory = $true)]
    [string]$PublishRoot,

    [Parameter(Mandatory = $true)]
    [string]$OutputRoot,

    [string]$WixPath = 'wix',

    [string]$IconPath = (Join-Path $PSScriptRoot '../../src/Lakona.Hub/Assets/lakona-hub.ico')
)

$ErrorActionPreference = 'Stop'
if (-not $IsWindows) {
    throw 'The Windows MSI must be built on Windows.'
}

$publishDirectory = (Resolve-Path -LiteralPath $PublishRoot).Path
$resolvedIconPath = (Resolve-Path -LiteralPath $IconPath).Path
$hubExecutable = Join-Path $publishDirectory 'Lakona.Hub.exe'
if (-not (Test-Path -LiteralPath $hubExecutable)) {
    throw "The win-x64 publish output is incomplete: $publishDirectory"
}

New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null
$outputDirectory = (Resolve-Path -LiteralPath $OutputRoot).Path
$workDirectory = Join-Path $outputDirectory '.wix'
if (Test-Path -LiteralPath $workDirectory) {
    Remove-Item -LiteralPath $workDirectory -Recurse -Force
}
New-Item -ItemType Directory -Path $workDirectory | Out-Null

$sourcePattern = [Security.SecurityElement]::Escape((Join-Path $publishDirectory '**'))
$executableSource = [Security.SecurityElement]::Escape($hubExecutable)
$iconSource = [Security.SecurityElement]::Escape($resolvedIconPath)
$source = Join-Path $workDirectory 'Lakona.Hub.wxs'
@"
<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">
  <Package Name="Lakona Hub" Manufacturer="Lakona" Version="$Version" UpgradeCode="D781E216-AC16-4F40-A74B-51AB25034019" Scope="perMachine">
    <MediaTemplate EmbedCab="yes" CompressionLevel="high" />
    <Icon Id="LakonaHubProductIcon" SourceFile="$iconSource" />
    <Property Id="ARPPRODUCTICON" Value="LakonaHubProductIcon" />
    <StandardDirectory Id="ProgramFiles64Folder">
      <Directory Id="INSTALLFOLDER" Name="Lakona Hub" />
    </StandardDirectory>
    <StandardDirectory Id="ProgramMenuFolder" />
    <Files Include="$sourcePattern" Directory="INSTALLFOLDER">
      <Exclude Files="$executableSource" />
    </Files>
    <Component Id="HubExecutableComponent" Directory="INSTALLFOLDER" Bitness="always64">
      <File Id="HubExecutable" Source="$executableSource" KeyPath="yes">
        <Shortcut Id="HubStartMenuShortcut" Directory="ProgramMenuFolder" Name="Lakona Hub" WorkingDirectory="INSTALLFOLDER" Advertise="no" />
      </File>
    </Component>
  </Package>
</Wix>
"@ | Set-Content -LiteralPath $source -Encoding utf8NoBOM

$packagePath = Join-Path $outputDirectory "lakona-hub-$Version-win-x64.msi"
try {
    & $WixPath build -arch x64 -o $packagePath $source
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $packagePath)) {
        throw 'WiX failed to create the Windows MSI.'
    }
}
finally {
    if (Test-Path -LiteralPath $workDirectory) {
        Remove-Item -LiteralPath $workDirectory -Recurse -Force
    }
}
