#Requires -Version 7.0
<#
.SYNOPSIS
    Prepare a Game.Unity.Agar single-node server for Unity MCP PlayMode tests.

.DESCRIPTION
    Verifies that Unity Editor and MCP for Unity are already running, builds
    Server.App and Server.Hotfix, starts the Agar server with dotnet run in
    single-node mode, and waits until the control endpoint is reachable.

    This script does not run Unity tests directly. Use Codex MCP tools after
    startup, then stop the server with -Stop.
#>

[CmdletBinding()]
param(
    [ValidateSet("Login", "Matchmaking", "Battle", "Settlement", "Smoke")]
    [string]$Scenario = "Smoke",

    [string]$HostName = "127.0.0.1",
    [int]$Port = 20000,
    [int]$KcpPort = 20001,
    [string]$Path = "/ws",
    [int]$TimeoutSeconds = 90,
    [string]$ArtifactRoot = "",
    [switch]$Restore,
    [switch]$StopExisting,
    [switch]$Stop
)

$ErrorActionPreference = "Stop"
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = (Resolve-Path (Join-Path $scriptRoot "../../..")).Path
$sampleRoot = Join-Path $repoRoot "samples/Game.Unity.Agar"
$serverAppProject = Join-Path $sampleRoot "Server/App/Server.App.csproj"
$serverHotfixProject = Join-Path $sampleRoot "Server/Hotfix/Server.Hotfix.csproj"
$artifactRoot = if ([string]::IsNullOrWhiteSpace($ArtifactRoot)) {
    Join-Path $repoRoot ".tmp/agar-single-node-unity-mcp"
}
else {
    $ArtifactRoot
}
$pidFile = Join-Path $artifactRoot "server.pid"
$stdoutLog = Join-Path $artifactRoot "server.out.log"
$stderrLog = Join-Path $artifactRoot "server.err.log"

function Write-Step {
    param([string]$Text)
    Write-Host ""
    Write-Host "== $Text ==" -ForegroundColor Cyan
}

function Stop-RecordedServer {
    if (-not (Test-Path -LiteralPath $pidFile)) {
        return
    }

    $pidValue = Get-Content -LiteralPath $pidFile -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not [string]::IsNullOrWhiteSpace($pidValue)) {
        Stop-Process -Id ([int]$pidValue) -Force -ErrorAction SilentlyContinue
    }

    Remove-Item -LiteralPath $pidFile -Force -ErrorAction SilentlyContinue
}

function Test-TcpPort {
    param(
        [string]$TargetHost,
        [int]$PortNumber
    )

    $client = [System.Net.Sockets.TcpClient]::new()
    try {
        $task = $client.ConnectAsync($TargetHost, $PortNumber)
        if (-not $task.Wait(1000)) {
            return $false
        }

        return $client.Connected
    }
    catch {
        return $false
    }
    finally {
        $client.Dispose()
    }
}

function Assert-UnityMcpReady {
    $unity = Get-Process -ErrorAction SilentlyContinue |
        Where-Object { $_.ProcessName -eq "Unity" } |
        Select-Object -First 1
    if ($null -eq $unity) {
        throw "Unity Editor is not running. Start samples/Game.Unity.Agar/Client in Unity and enable MCP for Unity before running this script."
    }

    if (-not (Test-TcpPort "127.0.0.1" 8180)) {
        throw "MCP for Unity is not reachable at 127.0.0.1:8180. Open the Unity project and start MCP for Unity, then rerun this script."
    }
}

function Invoke-DotNetBuild {
    param([string]$Project)

    $arguments = @("build", $Project, "-c", "Debug")
    if (-not $Restore) {
        $arguments += "--no-restore"
    }

    Write-Host "dotnet $($arguments -join ' ')"
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build failed for $Project with exit code $LASTEXITCODE."
    }
}

function Wait-ForServerReady {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        if ((Test-TcpPort $HostName $Port) -and
            (Test-Path -LiteralPath $stdoutLog) -and
            ((Get-Content -LiteralPath $stdoutLog -Raw -ErrorAction SilentlyContinue) -match "udp://.+:$KcpPort")) {
            return
        }

        $processAlive = $false
        if (Test-Path -LiteralPath $pidFile) {
            $pidValue = Get-Content -LiteralPath $pidFile -ErrorAction SilentlyContinue | Select-Object -First 1
            if (-not [string]::IsNullOrWhiteSpace($pidValue)) {
                $processAlive = $null -ne (Get-Process -Id ([int]$pidValue) -ErrorAction SilentlyContinue)
            }
        }

        if (-not $processAlive) {
            throw "Agar single-node server exited before becoming ready. See $stdoutLog and $stderrLog."
        }

        Start-Sleep -Milliseconds 500
    }

    throw "Timed out waiting for Agar single-node server on ${HostName}:$Port and udp port $KcpPort. See $stdoutLog and $stderrLog."
}

if ($TimeoutSeconds -lt 10) {
    throw "-TimeoutSeconds must be at least 10."
}

New-Item -ItemType Directory -Force -Path $artifactRoot | Out-Null

if ($Stop) {
    Write-Step "Stop Agar single-node server"
    Stop-RecordedServer
    Write-Host "Stopped recorded server if it was running."
    exit 0
}

Write-Step "Preflight Unity MCP"
Assert-UnityMcpReady
Write-Host "Unity Editor and MCP for Unity are reachable."

if ($StopExisting) {
    Write-Step "Stop previous Agar single-node server"
    Stop-RecordedServer
}
elseif (Test-Path -LiteralPath $pidFile) {
    $existingPid = Get-Content -LiteralPath $pidFile -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not [string]::IsNullOrWhiteSpace($existingPid) -and
        (Get-Process -Id ([int]$existingPid) -ErrorAction SilentlyContinue)) {
        throw "A recorded Agar server is already running with PID $existingPid. Pass -StopExisting or run this script with -Stop."
    }
}

Write-Step "Build Server.App"
Invoke-DotNetBuild $serverAppProject

Write-Step "Build Server.Hotfix"
Invoke-DotNetBuild $serverHotfixProject

Write-Step "Start single-node server"
Remove-Item -LiteralPath $stdoutLog, $stderrLog -Force -ErrorAction SilentlyContinue

$previousEnvironment = @{
    DOTNET_ENVIRONMENT = $env:DOTNET_ENVIRONMENT
    Lakona__Feature = $env:Lakona__Feature
    Lakona__Node__Id = $env:Lakona__Node__Id
    Lakona__Endpoints = $env:Lakona__Endpoints
}

try {
    $env:DOTNET_ENVIRONMENT = $null
    $env:Lakona__Feature = $null
    $env:Lakona__Node__Id = $null
    $env:Lakona__Endpoints = $null

    $runArgs = @(
        "run",
        "--project", $serverAppProject,
        "--configuration", "Debug",
        "--no-build"
    )
    $process = Start-Process -FilePath "dotnet" `
        -ArgumentList $runArgs `
        -WorkingDirectory $repoRoot `
        -RedirectStandardOutput $stdoutLog `
        -RedirectStandardError $stderrLog `
        -PassThru `
        -WindowStyle Hidden
    $process.Id | Set-Content -LiteralPath $pidFile -Encoding ASCII
}
finally {
    $env:DOTNET_ENVIRONMENT = $previousEnvironment.DOTNET_ENVIRONMENT
    $env:Lakona__Feature = $previousEnvironment.Lakona__Feature
    $env:Lakona__Node__Id = $previousEnvironment.Lakona__Node__Id
    $env:Lakona__Endpoints = $previousEnvironment.Lakona__Endpoints
}

Wait-ForServerReady

Write-Step "Ready"
Write-Host "Scenario: $Scenario"
Write-Host "Control endpoint: ws://${HostName}:$Port$Path"
Write-Host "Realtime KCP endpoint: udp://${HostName}:$KcpPort"
Write-Host "Server PID file: $pidFile"
Write-Host "Server stdout: $stdoutLog"
Write-Host "Server stderr: $stderrLog"
Write-Host ""
Write-Host "Next: run the Unity PlayMode test through MCP for Unity, then stop this server with:"
Write-Host "pwsh -NoProfile -File scripts/game/local/test-agar-single-node-unity-mcp.ps1 -Stop"
