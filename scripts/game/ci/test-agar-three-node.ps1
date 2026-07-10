#Requires -Version 7.0
<#
.SYNOPSIS
    Local-only three-node smoke test for samples/Game.Unity.Agar.

.DESCRIPTION
    Starts the real Docker Compose topology for data-1, gateway-1, battle-1,
    Postgres, and Redis, then runs the existing Unity client PlayMode smoke
    test in batchmode. This script is intentionally local-only and is not a
    default cloud CI gate.

.EXAMPLE
    pwsh -NoProfile -File scripts/game/ci/test-agar-three-node.ps1

.EXAMPLE
    pwsh -NoProfile -File scripts/game/ci/test-agar-three-node.ps1 -UnityPath "C:\Program Files\Unity\Hub\Editor\2022.3.62f1\Editor\Unity.exe" -KeepEnvironment
#>

[CmdletBinding()]
param(
    [string]$UnityPath = "",
    [string]$ProjectName = "lakona-agar-three-node-test",
    [int]$TimeoutSeconds = 600,
    [switch]$KeepEnvironment,
    [switch]$ReuseEnvironment,
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = (Resolve-Path (Join-Path $scriptRoot "../../..")).Path
$sampleRoot = Join-Path $repoRoot "samples/Game.Unity.Agar"
$clientRoot = Join-Path $sampleRoot "Client"
$composeFile = Join-Path $sampleRoot "docker-compose.yml"
$artifactRoot = Join-Path $repoRoot ".tmp/agar-three-node"
$overrideFile = Join-Path $artifactRoot "docker-compose.local-test.override.yml"
$testResults = Join-Path $artifactRoot "TestResults.xml"
$unityLog = Join-Path $artifactRoot "unity-editor.log"
$composeLog = Join-Path $artifactRoot "docker-compose.log"
$composeStartupLog = Join-Path $artifactRoot "docker-compose-startup.log"
$composeJson = Join-Path $artifactRoot "docker-compose.ps.json"
$unityResultValidator = Join-Path $scriptRoot "assert-unity-test-results.ps1"
$deadline = $null

function Write-Banner {
    param([string]$Text)
    $line = "=" * 72
    Write-Host ""
    Write-Host $line -ForegroundColor Cyan
    Write-Host "  $Text" -ForegroundColor Cyan
    Write-Host $line -ForegroundColor Cyan
}

function Invoke-Compose {
    param([string[]]$Arguments)
    & docker compose -p $ProjectName -f $composeFile -f $overrideFile @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "docker compose $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

function Invoke-ComposeStartup {
    param(
        [string[]]$Arguments,
        [int]$Timeout
    )

    if ($Timeout -le 0) {
        throw "docker compose up timed out after 0 seconds."
    }

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = "docker"
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true

    foreach ($argument in @("compose", "-p", $ProjectName, "-f", $composeFile, "-f", $overrideFile) + $Arguments) {
        [void]$startInfo.ArgumentList.Add($argument)
    }

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    $commandLine = "docker " + (($startInfo.ArgumentList | ForEach-Object { $_ }) -join " ")
    $timedOut = $false
    $exitCode = $null
    $stdout = ""
    $stderr = ""

    try {
        [void]$process.Start()
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()

        $timeoutMilliseconds = [Math]::Min(([int64]$Timeout * 1000), [int64][int]::MaxValue)
        if (-not $process.WaitForExit([int]$timeoutMilliseconds)) {
            $timedOut = $true
            try {
                $process.Kill($true)
            }
            catch {
                try {
                    $process.Kill()
                }
                catch {
                    Write-Host "  Could not kill docker compose startup process: $($_.Exception.Message)" -ForegroundColor DarkYellow
                }
            }

            $process.WaitForExit()
        }
        else {
            $process.WaitForExit()
            $exitCode = $process.ExitCode
        }

        $stdout = $stdoutTask.GetAwaiter().GetResult()
        $stderr = $stderrTask.GetAwaiter().GetResult()
    }
    finally {
        $process.Dispose()
    }

    @(
        "Command: $commandLine"
        "Timed out: $timedOut"
        "Exit code: $exitCode"
        ""
        "STDOUT:"
        $stdout
        ""
        "STDERR:"
        $stderr
    ) | Set-Content -LiteralPath $composeStartupLog -Encoding UTF8

    if ($timedOut) {
        throw "docker compose up timed out after $Timeout seconds. Startup log: $composeStartupLog"
    }

    if ($exitCode -ne 0) {
        throw "docker compose $($Arguments -join ' ') failed with exit code $exitCode. Startup log: $composeStartupLog"
    }
}

function Test-Command {
    param([string]$Command)
    $existing = Get-Command $Command -ErrorAction SilentlyContinue
    return $null -ne $existing
}

function Get-UnityProjectEditorVersion {
    $projectVersionFile = Join-Path $clientRoot "ProjectSettings\ProjectVersion.txt"
    if (-not (Test-Path -LiteralPath $projectVersionFile)) {
        return ""
    }

    foreach ($line in Get-Content -LiteralPath $projectVersionFile -ErrorAction SilentlyContinue) {
        if ($line -match '^\s*m_EditorVersion:\s*(?<Version>\S+)\s*$') {
            return $Matches["Version"]
        }
    }

    return ""
}

function Get-UnityVersionSortKey {
    param([string]$Version)

    if ($Version -match '^(?<Major>\d+)\.(?<Minor>\d+)\.(?<Patch>\d+)') {
        return "{0:D6}.{1:D6}.{2:D6}.{3}" -f [int]$Matches["Major"], [int]$Matches["Minor"], [int]$Matches["Patch"], $Version
    }

    return "000000.000000.000000.$Version"
}

function Add-UnityCandidate {
    param(
        [System.Collections.Generic.List[string]]$Candidates,
        [string]$Path
    )

    if (-not [string]::IsNullOrWhiteSpace($Path)) {
        $Candidates.Add($Path)
    }
}

function Resolve-UnityExecutable {
    param([string]$ExplicitPath)

    $candidates = New-Object System.Collections.Generic.List[string]
    if (-not [string]::IsNullOrWhiteSpace($ExplicitPath)) {
        if (-not (Test-Path -LiteralPath $ExplicitPath)) {
            throw "Unity executable was not found. Pass -UnityPath or set UNITY_PATH."
        }

        Add-UnityCandidate $candidates $ExplicitPath
    }

    Add-UnityCandidate $candidates $env:UNITY_PATH

    $hubRoot = "C:\Program Files\Unity\Hub\Editor"
    $projectEditorVersion = Get-UnityProjectEditorVersion
    if (-not [string]::IsNullOrWhiteSpace($projectEditorVersion)) {
        Add-UnityCandidate $candidates (Join-Path $hubRoot "$projectEditorVersion\Editor\Unity.exe")
    }

    if (Test-Path $hubRoot) {
        Get-ChildItem -LiteralPath $hubRoot -Directory -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -like "2022.3.*" } |
            Sort-Object @{ Expression = { Get-UnityVersionSortKey $_.Name }; Descending = $true } |
            ForEach-Object {
                Add-UnityCandidate $candidates (Join-Path $_.FullName "Editor\Unity.exe")
            }

        Get-ChildItem -LiteralPath $hubRoot -Directory -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -notlike "2022.3.*" } |
            Sort-Object @{ Expression = { Get-UnityVersionSortKey $_.Name }; Descending = $true } |
            ForEach-Object {
                Add-UnityCandidate $candidates (Join-Path $_.FullName "Editor\Unity.exe")
            }
    }

    $legacy = "C:\Program Files\Unity\Editor\Unity.exe"
    Add-UnityCandidate $candidates $legacy

    foreach ($candidate in $candidates) {
        if (-not [string]::IsNullOrWhiteSpace($candidate) -and (Test-Path -LiteralPath $candidate)) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }

    throw "Unity executable was not found. Pass -UnityPath or set UNITY_PATH."
}

function Write-OverrideComposeFile {
    New-Item -ItemType Directory -Force -Path $artifactRoot | Out-Null
    @"
services:
  data-1:
    container_name: lakona-agar-three-node-test-data-1
    environment:
      Lakona__Cluster__Endpoint: tcp://10.10.0.1:21001
      Lakona__Cluster__Seeds: '["tcp://10.10.0.1:21001"]'
    networks:
      agar-cluster:
        ipv4_address: 10.10.0.1
  gateway-1:
    container_name: lakona-agar-three-node-test-gateway-1
    environment:
      Lakona__Endpoints: >-
        [
          {
            "Transport": "websocket",
            "Serializer": "memorypack",
            "Host": "0.0.0.0",
            "AdvertisedHost": "127.0.0.1",
            "Port": 20000,
            "Path": "/ws",
            "RpcServices": [ "login", "player" ]
          }
        ]
      Lakona__Cluster__Endpoint: tcp://10.10.0.2:21002
      Lakona__Cluster__Seeds: '["tcp://10.10.0.1:21001"]'
    networks:
      agar-cluster:
        ipv4_address: 10.10.0.2
  battle-1:
    container_name: lakona-agar-three-node-test-battle-1
    environment:
      Lakona__Endpoints: >-
        [
          {
            "Transport": "kcp",
            "Serializer": "memorypack",
            "Host": "0.0.0.0",
            "AdvertisedHost": "127.0.0.1",
            "Port": 20001,
            "RpcServices": [ "battle" ]
          }
        ]
      Lakona__Cluster__Endpoint: tcp://10.10.0.3:21003
      Lakona__Cluster__Seeds: '["tcp://10.10.0.1:21001"]'
    networks:
      agar-cluster:
        ipv4_address: 10.10.0.3
  postgres:
    container_name: lakona-agar-three-node-test-postgres
    ports: !reset []
  redis:
    container_name: lakona-agar-three-node-test-redis
    ports: !reset []
networks:
  agar-cluster:
    ipam:
      config:
        - subnet: 10.10.0.0/24
          gateway: 10.10.0.254
          ip_range: 10.10.0.128/25
"@ | Set-Content -LiteralPath $overrideFile -Encoding UTF8
}

function Wait-Until {
    param(
        [string]$Description,
        [scriptblock]$Predicate,
        [int]$Timeout
    )

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($Timeout)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        if (& $Predicate) {
            Write-Host "  OK: $Description" -ForegroundColor Green
            return
        }

        Start-Sleep -Seconds 2
    }

    throw "Timed out waiting for $Description."
}

function Get-RemainingSeconds {
    if ($null -eq $script:deadline) {
        throw "Timeout deadline has not been initialized."
    }

    $remaining = [int][Math]::Floor(($script:deadline - [DateTimeOffset]::UtcNow).TotalSeconds)
    if ($remaining -le 0) {
        throw "Timed out after $TimeoutSeconds seconds."
    }

    return $remaining
}

function Get-ContainerId {
    param([string]$Service)
    $id = & docker compose -p $ProjectName -f $composeFile -f $overrideFile ps -q $Service
    if ($LASTEXITCODE -ne 0) {
        return ""
    }

    return ($id | Select-Object -First 1)
}

function Test-ServiceRunning {
    param([string]$Service)
    $id = Get-ContainerId $Service
    if ([string]::IsNullOrWhiteSpace($id)) {
        return $false
    }

    $state = & docker inspect --format "{{.State.Status}}" $id 2>$null
    return $LASTEXITCODE -eq 0 -and $state -eq "running"
}

function Test-ServiceHealthy {
    param([string]$Service)
    $id = Get-ContainerId $Service
    if ([string]::IsNullOrWhiteSpace($id)) {
        return $false
    }

    $health = & docker inspect --format "{{if .State.Health}}{{.State.Health.Status}}{{else}}{{.State.Status}}{{end}}" $id 2>$null
    return $LASTEXITCODE -eq 0 -and ($health -eq "healthy" -or $health -eq "running")
}

function Test-TcpPort {
    param(
        [string]$HostName,
        [int]$Port
    )

    $client = [System.Net.Sockets.TcpClient]::new()
    try {
        $task = $client.ConnectAsync($HostName, $Port)
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

function Test-TcpPortFree {
    param([int]$Port)
    $listener = $null
    try {
        $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Any, $Port)
        $listener.Start()
        return $true
    }
    catch {
        return $false
    }
    finally {
        if ($null -ne $listener) {
            $listener.Stop()
        }
    }
}

function Test-UdpPortFree {
    param([int]$Port)
    $client = $null
    try {
        $client = [System.Net.Sockets.UdpClient]::new($Port)
        return $true
    }
    catch {
        return $false
    }
    finally {
        if ($null -ne $client) {
            $client.Dispose()
        }
    }
}

function Get-DockerPublishedPortOwner {
    param(
        [int]$Port,
        [string]$Protocol
    )

    $containers = & docker ps --format "{{.Names}}|{{.Ports}}" 2>$null
    if ($LASTEXITCODE -ne 0) {
        return ""
    }

    foreach ($container in $containers) {
        $parts = $container -split "\|", 2
        if ($parts.Count -ne 2) {
            continue
        }

        $ports = $parts[1]
        if ($ports.Contains(":$Port->", [StringComparison]::Ordinal) -and
            $ports.Contains("/$Protocol", [StringComparison]::OrdinalIgnoreCase)) {
            return $parts[0]
        }
    }

    return ""
}

function Test-DockerPublishedPortFree {
    param(
        [int]$Port,
        [string]$Protocol
    )

    return [string]::IsNullOrWhiteSpace((Get-DockerPublishedPortOwner $Port $Protocol))
}

function Assert-RequiredPortsFree {
    $gatewayOwner = Get-DockerPublishedPortOwner 20000 "tcp"
    if (-not (Test-TcpPortFree 20000) -or -not (Test-DockerPublishedPortFree 20000 "tcp")) {
        $suffix = [string]::IsNullOrWhiteSpace($gatewayOwner) ? "" : " Docker container '$gatewayOwner' publishes this port."
        throw "Port 20000/tcp is already in use.$suffix Stop the existing Agar gateway or run the script on a host with that port free."
    }

    $battleOwner = Get-DockerPublishedPortOwner 20001 "udp"
    if (-not (Test-UdpPortFree 20001) -or -not (Test-DockerPublishedPortFree 20001 "udp")) {
        $suffix = [string]::IsNullOrWhiteSpace($battleOwner) ? "" : " Docker container '$battleOwner' publishes this port."
        throw "Port 20001/udp is already in use.$suffix Stop the existing Agar battle node or run the script on a host with that port free."
    }
}

function Save-ComposeArtifacts {
    try {
        $composeStatus = & docker compose -p $ProjectName -f $composeFile -f $overrideFile ps --format json 2>&1
        $exitCode = $LASTEXITCODE
        if ($exitCode -eq 0) {
            $composeStatus | Set-Content -LiteralPath $composeJson -Encoding UTF8
        }
        else {
            @(
                "WARNING: docker compose ps --format json failed with exit code $exitCode."
                "Command output:"
                $composeStatus
            ) | Set-Content -LiteralPath $composeJson -Encoding UTF8
        }
    }
    catch {
        Write-Host "  Could not write compose status JSON: $($_.Exception.Message)" -ForegroundColor DarkYellow
    }

    try {
        $composeLogs = & docker compose -p $ProjectName -f $composeFile -f $overrideFile logs --no-color 2>&1
        $exitCode = $LASTEXITCODE
        if ($exitCode -eq 0) {
            $composeLogs | Set-Content -LiteralPath $composeLog -Encoding UTF8
        }
        else {
            @(
                "WARNING: docker compose logs --no-color failed with exit code $exitCode."
                "Command output:"
                $composeLogs
            ) | Set-Content -LiteralPath $composeLog -Encoding UTF8
        }
    }
    catch {
        Write-Host "  Could not write compose logs: $($_.Exception.Message)" -ForegroundColor DarkYellow
    }
}

function Show-LogTail {
    param(
        [string]$Path,
        [string]$Label
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }

    Write-Host ""
    Write-Host "$Label tail:" -ForegroundColor Yellow
    Get-Content -LiteralPath $Path -Tail 120
}

function Run-UnityPlayModeTest {
    param(
        [string]$UnityExecutable,
        [int]$Timeout
    )

    $targetTest = "SampleClient.Gameplay.Tests.DotArenaThreeNodePlayModeTests.UnityClientCompletesThreeNodeMultiplayerSmoke"
    $unityArgs = @(
        "-batchmode",
        "-projectPath", $clientRoot,
        "-runTests",
        "-testPlatform", "PlayMode",
        "-testFilter", $targetTest,
        "-testResults", $testResults,
        "-logFile", $unityLog,
        "--host", "127.0.0.1",
        "--port", "20000",
        "--path", "/ws"
    )

    Write-Host "  Unity: $UnityExecutable"
    Write-Host "  Project: $clientRoot"
    Remove-Item -LiteralPath $testResults -Force -ErrorAction SilentlyContinue
    $process = Start-Process -FilePath $UnityExecutable -ArgumentList $unityArgs -PassThru -NoNewWindow
    if (-not $process.WaitForExit($Timeout * 1000)) {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        throw "Unity PlayMode test timed out after $Timeout seconds."
    }

    $process.Refresh()
    if ($process.ExitCode -ne 0) {
        throw "Unity PlayMode test failed with exit code $($process.ExitCode)."
    }

    & $unityResultValidator -ResultsPath $testResults -TargetTestName $targetTest
}

if ($TimeoutSeconds -lt 60) {
    throw "-TimeoutSeconds must be at least 60."
}

New-Item -ItemType Directory -Force -Path $artifactRoot | Out-Null
Write-OverrideComposeFile

$unity = ""
$failed = $false
$composeStarted = $false

try {
    Write-Banner "Preflight"
    $unity = Resolve-UnityExecutable $UnityPath
    Write-Host "  Unity executable: $unity" -ForegroundColor Green

    if (-not (Test-Command "docker")) {
        throw "docker compose is not available because docker was not found on PATH."
    }

    & docker compose version | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "docker compose is not available."
    }

    if (-not (Test-Path -LiteralPath $composeFile)) {
        throw "Compose file was not found: $composeFile"
    }

    if (-not (Test-Path -LiteralPath $clientRoot)) {
        throw "Unity client project was not found: $clientRoot"
    }

    Assert-RequiredPortsFree

    $script:deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)

    Write-Banner "Start Agar three-node topology"
    if (-not $ReuseEnvironment) {
        & docker compose -p $ProjectName -f $composeFile -f $overrideFile down --volumes --remove-orphans
        if ($LASTEXITCODE -ne 0) {
            throw "docker compose down failed before startup."
        }
    }

    $upArgs = @("up", "-d")
    if (-not $SkipBuild) {
        $upArgs += "--build"
    }

    $composeStarted = $true
    Invoke-ComposeStartup $upArgs (Get-RemainingSeconds)

    Write-Banner "Wait for readiness"
    Wait-Until "Postgres healthy" { Test-ServiceHealthy "postgres" } (Get-RemainingSeconds)
    Wait-Until "Redis healthy" { Test-ServiceHealthy "redis" } (Get-RemainingSeconds)
    Wait-Until "data-1 running" { Test-ServiceRunning "data-1" } (Get-RemainingSeconds)
    Wait-Until "gateway-1 running" { Test-ServiceRunning "gateway-1" } (Get-RemainingSeconds)
    Wait-Until "battle-1 running" { Test-ServiceRunning "battle-1" } (Get-RemainingSeconds)
    Wait-Until "gateway port 20000 reachable" { Test-TcpPort "127.0.0.1" 20000 } (Get-RemainingSeconds)

    Write-Banner "Run Unity PlayMode smoke"
    Run-UnityPlayModeTest $unity (Get-RemainingSeconds)

    Write-Banner "Agar three-node local test passed"
    Write-Host "  Test results: $testResults" -ForegroundColor Green
    Write-Host "  Unity log:    $unityLog" -ForegroundColor Green
    Write-Host "  Compose log:  $composeLog" -ForegroundColor Green
}
catch {
    $failed = $true
    Write-Host ""
    Write-Host "Agar three-node local test failed: $($_.Exception.Message)" -ForegroundColor Red
    if ($composeStarted) {
        Save-ComposeArtifacts
        Show-LogTail $unityLog "Unity editor log"
        Show-LogTail $composeLog "Docker Compose log"
    }

    Write-Host ""
    Write-Host "Artifacts: $artifactRoot" -ForegroundColor Yellow
    throw
}
finally {
    if ($composeStarted -and -not $failed) {
        Save-ComposeArtifacts
    }

    if ($KeepEnvironment -or $ReuseEnvironment) {
        Write-Host "Preserving Docker Compose environment: project=$ProjectName" -ForegroundColor Yellow
    }
    elseif ($composeStarted) {
        Write-Banner "Cleanup"
        & docker compose -p $ProjectName -f $composeFile -f $overrideFile down --volumes --remove-orphans
        if ($LASTEXITCODE -ne 0) {
            Write-Host "docker compose down failed during cleanup." -ForegroundColor Red
        }
    }
}
