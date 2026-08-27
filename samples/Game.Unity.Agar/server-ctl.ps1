#Requires -Version 7.0
<#
.SYNOPSIS
    Controls the local Game.Unity.Agar server cluster.

.DESCRIPTION
    Starts, inspects, stops, and tails logs for the Docker Compose topology.
    Start selects either the single-node development topology or the complete
    three-node topology. Three-node is the default.
    The start command succeeds only after every Lakona node reports ready from
    its /_lakona/health/ready management endpoint.

.EXAMPLE
    pwsh -NoProfile -File ./server-ctl.ps1 start

.EXAMPLE
    pwsh -NoProfile -File ./server-ctl.ps1 start -Topology single

.EXAMPLE
    pwsh -NoProfile -File ./server-ctl.ps1 logs gateway-1
#>

[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet("start", "status", "stop", "logs", "help")]
    [string]$Command = "help",

    [Parameter(Position = 1, ValueFromRemainingArguments)]
    [string[]]$Services = @(),

    [ValidateRange(10, 1800)]
    [int]$TimeoutSeconds = 300,

    [ValidateRange(1, 10000)]
    [int]$Tail = 200,

    [ValidateSet("single", "three")]
    [string]$Topology = "three",

    [switch]$NoBuild,
    [switch]$NoFollow
)

$ErrorActionPreference = "Stop"
$sampleRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$composeFile = Join-Path $sampleRoot "docker-compose.yml"

function Write-Usage {
    Write-Host @"
Game.Unity.Agar server control

Usage:
  pwsh -NoProfile -File ./server-ctl.ps1 start [-Topology single|three] [-NoBuild] [-TimeoutSeconds <seconds>]
  pwsh -NoProfile -File ./server-ctl.ps1 status [-Topology single|three]
  pwsh -NoProfile -File ./server-ctl.ps1 stop
  pwsh -NoProfile -File ./server-ctl.ps1 logs [service ...] [-Tail <lines>] [-NoFollow]
  pwsh -NoProfile -File ./server-ctl.ps1 help

Commands:
  start   Start the selected Compose topology and wait for every Lakona node
          to return HTTP 200 from /_lakona/health/ready. Defaults to three.
  status  Show Compose state and probe every Lakona readiness endpoint.
  stop    Stop and remove the Compose containers and network. Volumes are kept.
  logs    Follow recent Compose logs. Optionally select services such as
          single-1, data-1, gateway-1, battle-1, postgres, or redis.
  help    Show this help.
"@
}

function Invoke-Compose {
    param(
        [Parameter(Mandatory)]
        [string[]]$Arguments
    )

    # single-1 is profile-gated so plain `docker compose up` remains the
    # established three-node topology used by the dedicated E2E script.
    & docker compose --file $composeFile --profile single @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "docker compose $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

function Assert-DockerReady {
    if ($null -eq (Get-Command docker -ErrorAction SilentlyContinue)) {
        throw "Docker was not found. Install Docker Desktop and ensure 'docker' is available on PATH."
    }

    & docker info --format "{{.ServerVersion}}" | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Docker is not available. Start Docker Desktop and retry."
    }
}

function Get-ConfiguredPort {
    param(
        [Parameter(Mandatory)]
        [string]$EnvironmentVariable,

        [Parameter(Mandatory)]
        [int]$Default
    )

    $value = [Environment]::GetEnvironmentVariable($EnvironmentVariable)
    if ([string]::IsNullOrWhiteSpace($value)) {
        return $Default
    }

    $port = 0
    if (-not [int]::TryParse($value, [ref]$port) -or $port -lt 1 -or $port -gt 65535) {
        throw "$EnvironmentVariable must be a TCP port between 1 and 65535."
    }

    return $port
}

function Get-ReadinessTargets {
    if ($Topology -eq "single") {
        return @(
            [pscustomobject]@{
                Name = "single-1"
                Url = "http://127.0.0.1:$(Get-ConfiguredPort -EnvironmentVariable 'AGAR_GATEWAY_MANAGEMENT_PORT' -Default 20080)/_lakona/health/ready"
            }
        )
    }

    @(
        [pscustomobject]@{
            Name = "gateway-1"
            Url = "http://127.0.0.1:$(Get-ConfiguredPort -EnvironmentVariable 'AGAR_GATEWAY_MANAGEMENT_PORT' -Default 20080)/_lakona/health/ready"
        }
        [pscustomobject]@{
            Name = "data-1"
            Url = "http://127.0.0.1:$(Get-ConfiguredPort -EnvironmentVariable 'AGAR_DATA_MANAGEMENT_PORT' -Default 20081)/_lakona/health/ready"
        }
        [pscustomobject]@{
            Name = "battle-1"
            Url = "http://127.0.0.1:$(Get-ConfiguredPort -EnvironmentVariable 'AGAR_BATTLE_MANAGEMENT_PORT' -Default 20082)/_lakona/health/ready"
        }
    )
}

function Get-TopologyServices {
    if ($Topology -eq "single") {
        return @("postgres", "redis", "single-1")
    }

    return @("postgres", "redis", "data-1", "gateway-1", "battle-1")
}

function Get-AllGameServices {
    return @("single-1", "data-1", "gateway-1", "battle-1")
}

function Get-EnvironmentSetting {
    param(
        [Parameter(Mandatory)]
        [string]$Name,

        [Parameter(Mandatory)]
        [string]$Default
    )

    $value = [Environment]::GetEnvironmentVariable($Name)
    if ([string]::IsNullOrWhiteSpace($value)) {
        return $Default
    }

    return $value
}

function Test-ReadinessEndpoint {
    param(
        [Parameter(Mandatory)]
        [string]$Url
    )

    try {
        $response = Invoke-WebRequest -Uri $Url -Method Get -TimeoutSec 2 -UseBasicParsing
        return $response.StatusCode -eq 200
    }
    catch {
        return $false
    }
}

function Show-Readiness {
    $allReady = $true
    foreach ($target in Get-ReadinessTargets) {
        $ready = Test-ReadinessEndpoint -Url $target.Url
        $state = if ($ready) { "ready" } else { "not ready" }
        $color = if ($ready) { "Green" } else { "Yellow" }
        Write-Host ("{0,-10} {1,-9} {2}" -f $target.Name, $state, $target.Url) -ForegroundColor $color
        $allReady = $allReady -and $ready
    }

    return $allReady
}

function Wait-ForClusterReady {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    $pending = [System.Collections.Generic.HashSet[string]]::new(
        [string[]]((Get-ReadinessTargets).Name),
        [StringComparer]::Ordinal)

    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        foreach ($target in Get-ReadinessTargets) {
            if ($pending.Contains($target.Name) -and (Test-ReadinessEndpoint -Url $target.Url)) {
                $pending.Remove($target.Name) | Out-Null
                Write-Host "$($target.Name) ready: $($target.Url)" -ForegroundColor Green
            }
        }

        if ($pending.Count -eq 0) {
            return
        }

        Start-Sleep -Milliseconds 500
    }

    throw "Timed out after $TimeoutSeconds seconds waiting for readiness: $($pending -join ', ')."
}

switch ($Command) {
    "start" {
        Assert-DockerReady
        Invoke-Compose -Arguments (@("stop") + (Get-AllGameServices))
        Invoke-Compose -Arguments @("up", "--detach", "--wait", "postgres", "redis")
        $arguments = @("up", "--detach")
        if (-not $NoBuild) {
            $arguments += "--build"
        }
        $arguments += Get-TopologyServices

        Invoke-Compose -Arguments $arguments
        try {
            Wait-ForClusterReady
        }
        catch {
            Write-Host ""
            Write-Host "Cluster did not become ready. Current state:" -ForegroundColor Red
            Invoke-Compose -Arguments @("ps")
            Write-Host ""
            Write-Host "Recent logs:" -ForegroundColor Red
            Invoke-Compose -Arguments @("logs", "--tail", "100")
            throw
        }

        Write-Host ""
        Write-Host "Game.Unity.Agar $Topology topology is ready." -ForegroundColor Green
        Write-Host "Gateway:    ws://127.0.0.1:$(Get-ConfiguredPort -EnvironmentVariable 'AGAR_GATEWAY_PORT' -Default 20000)/ws"
        Write-Host "Battle KCP: udp://127.0.0.1:$(Get-ConfiguredPort -EnvironmentVariable 'AGAR_BATTLE_PORT' -Default 20001)"
        $operationsHost = if ([string]::IsNullOrWhiteSpace($env:AGAR_OPERATIONS_BIND_HOST)) {
            "127.0.0.1"
        }
        else {
            $env:AGAR_OPERATIONS_BIND_HOST
        }
        Write-Host "Operations: http://${operationsHost}:$(Get-ConfiguredPort -EnvironmentVariable 'AGAR_OPERATIONS_PORT' -Default 21000)"
    }
    "status" {
        Assert-DockerReady
        Invoke-Compose -Arguments (@("ps") + (Get-TopologyServices))
        Write-Host ""
        if (-not (Show-Readiness)) {
            exit 1
        }
    }
    "stop" {
        Assert-DockerReady
        Invoke-Compose -Arguments @("down")
        Write-Host "Game.Unity.Agar stopped. PostgreSQL and Redis volumes were kept."
    }
    "logs" {
        Assert-DockerReady
        $arguments = @("logs", "--tail", $Tail.ToString())
        if (-not $NoFollow) {
            $arguments += "--follow"
        }
        $arguments += $Services
        Invoke-Compose -Arguments $arguments
    }
    "help" {
        Write-Usage
    }
}
