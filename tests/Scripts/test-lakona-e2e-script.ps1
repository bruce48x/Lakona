#Requires -Version 5.1

$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptRoot "../..")
$target = Join-Path $repoRoot ".agents/skills/lakona-e2e-testing/scripts/run-e2e.ps1"
$content = Get-Content -Raw -LiteralPath $target

$functionMatch = [regex]::Match(
    $content,
    '(?s)function Set-GeneratedServerPort \{.*?(?=\r?\nfunction Test-PortAvailable)')

if (-not $functionMatch.Success) {
    throw "Could not find Set-GeneratedServerPort in $target"
}

Invoke-Expression $functionMatch.Value

$dependencyPatchMatch = [regex]::Match(
    $content,
    '(?s)function Patch-ServerDependencies \{.*?(?=\r?\n# ═+\r?\n# Main)')

if (-not $dependencyPatchMatch.Success) {
    throw "Could not find Patch-ServerDependencies in $target"
}

Invoke-Expression $dependencyPatchMatch.Value

$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("lakona-e2e-script-test-" + [guid]::NewGuid().ToString("N"))
$appDir = Join-Path $tempRoot "Server/App"
$hotfixDir = Join-Path $tempRoot "Server/Hotfix"
$sharedDir = Join-Path $tempRoot "Shared"
$fixtureRepoRoot = Join-Path $tempRoot "repo"
$appSettings = Join-Path $appDir "appsettings.json"

try {
    New-Item -ItemType Directory -Path $appDir -Force | Out-Null
    New-Item -ItemType Directory -Path $hotfixDir -Force | Out-Null
    New-Item -ItemType Directory -Path $sharedDir -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $fixtureRepoRoot "src/Lakona.Game.Server") -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $fixtureRepoRoot "src/Lakona.Game.Server.Hotfix.Generators") -Force | Out-Null
    @'
{
  "Lakona": {
    "Management": {
      "Http": {
        "Port": 20080
      }
    },
    "Endpoints": [
      {
        "Transport": "websocket",
        "Port": 20000
      }
    ]
  }
}
'@ | Set-Content -LiteralPath $appSettings -Encoding UTF8

    '<Project />' |
        Set-Content -LiteralPath (Join-Path $sharedDir "Shared.csproj") -Encoding UTF8
    '<Project />' |
        Set-Content -LiteralPath (Join-Path $fixtureRepoRoot "src/Lakona.Game.Server/Lakona.Game.Server.csproj") -Encoding UTF8
    '<Project />' |
        Set-Content -LiteralPath (Join-Path $fixtureRepoRoot "src/Lakona.Game.Server.Hotfix.Generators/Lakona.Game.Server.Hotfix.Generators.csproj") -Encoding UTF8

    @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <LakonaProjectRole>ServerApp</LakonaProjectRole>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Lakona.Game.Server" Version="1.0.0" />
  </ItemGroup>
</Project>
'@ | Set-Content -LiteralPath (Join-Path $appDir "Server.App.csproj") -Encoding UTF8

    @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <LakonaProjectRole>Hotfix</LakonaProjectRole>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\App\Server.App.csproj" />
  </ItemGroup>
</Project>
'@ | Set-Content -LiteralPath (Join-Path $hotfixDir "Server.Hotfix.csproj") -Encoding UTF8

    Set-GeneratedServerPort -ProjectDir $tempRoot -Port 20137
    Patch-ServerDependencies -ProjectDir $tempRoot -RepoRoot $fixtureRepoRoot

    $config = Get-Content -Raw -LiteralPath $appSettings | ConvertFrom-Json
    if ($config.Lakona.Management.Http.Port -ne 20080) {
        throw "Management HTTP port must remain unchanged."
    }

    if ($config.Lakona.Endpoints[0].Port -ne 20137) {
        throw "RPC endpoint port was not updated."
    }

    if ($content -notmatch '\$serverText -match "Lakona server started successfully"') {
        throw "Server readiness must recognize the transport-neutral Lakona startup signal."
    }

    $appProjectContent = Get-Content -Raw -LiteralPath (Join-Path $appDir "Server.App.csproj")
    if ($appProjectContent -notmatch 'Lakona\.Game\.Server\.csproj') {
        throw "ProjectReference mode must replace the App Game.Server package with its project."
    }

    $hotfixProjectContent = Get-Content -Raw -LiteralPath (Join-Path $hotfixDir "Server.Hotfix.csproj")
    if ($hotfixProjectContent -match 'Lakona\.Game\.Server(?:\.Hotfix\.Abstractions)?\.csproj') {
        throw "Hotfix must inherit Game.Server through Server.App without a direct framework project reference."
    }

    foreach ($projectContent in @($appProjectContent, $hotfixProjectContent)) {
        if ($projectContent -notmatch 'Lakona\.Game\.Server\.Hotfix\.Generators\.csproj') {
            throw "ProjectReference mode must retain the Hotfix generator analyzer."
        }
    }

    Write-Host "Lakona scaffold E2E script contract: PASS"
}
finally {
    if (Test-Path -LiteralPath $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force
    }
}
