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

$portFunctionMatch = [regex]::Match(
    $content,
    '(?s)function Test-PortAvailable \{.*?(?=\r?\nfunction Find-AvailablePortBase)')

if (-not $portFunctionMatch.Success) {
    throw "Could not find Test-PortAvailable in $target"
}

Invoke-Expression $portFunctionMatch.Value

$portSelectionFunctionMatch = [regex]::Match(
    $content,
    '(?s)function Find-AvailablePortBase \{.*?(?=\r?\nfunction Stop-ProcessTree)')

if (-not $portSelectionFunctionMatch.Success) {
    throw "Could not find Find-AvailablePortBase in $target"
}

Invoke-Expression $portSelectionFunctionMatch.Value

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
$tcpListener = $null
$udpSocket = $null

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
    if ($config.Lakona.Management.Http.Port -ne 22137) {
        throw "Management HTTP port was not updated."
    }

    if ($config.Lakona.Endpoints[0].Port -ne 20137) {
        throw "RPC endpoint port was not updated."
    }

    if ($config.Lakona.Cluster.Endpoint -ne "tcp://127.0.0.1:21137") {
        throw "Cluster endpoint port was not updated."
    }

    if ($content -notmatch '\$serverText -match "Lakona server started successfully"') {
        throw "Server readiness must recognize the transport-neutral Lakona startup signal."
    }

    if ($content -match '\$serverText -match "Application started"') {
        throw "Server readiness must not treat the earlier ASP.NET startup signal as Lakona readiness."
    }

    $tcpListener = [System.Net.Sockets.TcpListener]::new(
        [System.Net.IPAddress]::Loopback,
        0)
    $tcpListener.Start()
    $occupiedTcpPort = ([System.Net.IPEndPoint] $tcpListener.LocalEndpoint).Port
    if (Test-PortAvailable -Port $occupiedTcpPort) {
        throw "An occupied TCP port must not be reported as available."
    }
    $tcpListener.Stop()
    $tcpListener = $null

    $occupiedUdpPort = $null
    for ($candidatePort = 20000; $candidatePort -le 63535; $candidatePort++) {
        $candidateSocket = [System.Net.Sockets.Socket]::new(
            [System.Net.Sockets.AddressFamily]::InterNetwork,
            [System.Net.Sockets.SocketType]::Dgram,
            [System.Net.Sockets.ProtocolType]::Udp)
        $candidateSocket.ExclusiveAddressUse = $true
        try {
            $candidateSocket.Bind([System.Net.IPEndPoint]::new(
                [System.Net.IPAddress]::Loopback,
                $candidatePort))
            $udpSocket = $candidateSocket
            $occupiedUdpPort = $candidatePort
            break
        } catch [System.Net.Sockets.SocketException] {
            $candidateSocket.Dispose()
            continue
        }
    }

    if ($null -eq $occupiedUdpPort) {
        throw "Could not reserve a safe UDP fixture port."
    }

    if (Test-PortAvailable -Port $occupiedUdpPort) {
        throw "An occupied UDP port must not be reported as available."
    }

    $selectedPort = Find-AvailablePortBase -PreferredPort $occupiedUdpPort -CaseCount 1
    if ($selectedPort -eq $occupiedUdpPort) {
        throw "Automatic port selection must skip an occupied preferred port."
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
    if ($tcpListener) {
        $tcpListener.Stop()
    }

    if ($udpSocket) {
        $udpSocket.Dispose()
    }

    if (Test-Path -LiteralPath $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force
    }
}
