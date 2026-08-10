#Requires -Version 7.0

[CmdletBinding()]
param(
    [ValidateSet("tcp", "websocket", "kcp")]
    [string]$Transport = "kcp",

    [ValidateSet("json", "memorypack")]
    [string]$Serializer = "memorypack",

    [string]$GodotBin = $env:GODOT_BIN,

    [string]$GodotNupkgs = $env:GODOT_NUPKGS
)

$ErrorActionPreference = "Stop"
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$rootDir = (Resolve-Path (Join-Path $scriptRoot "../../..")).Path
$workDir = Join-Path $rootDir ".tmp/lakona-tool-godot-daily"
$generatedRoot = Join-Path $workDir "generated"
$logDir = Join-Path $workDir "logs"
$localFeed = Join-Path $rootDir "artifacts/ci-nuget"
$ciNuGetConfig = Join-Path $workDir "NuGet.config"
$packageCache = Join-Path $workDir "packages"

if (-not $PSBoundParameters.ContainsKey("Transport") -and
    -not [string]::IsNullOrWhiteSpace($env:LAKONA_TOOL_TRANSPORT)) {
    $Transport = $env:LAKONA_TOOL_TRANSPORT
}
if (-not $PSBoundParameters.ContainsKey("Serializer") -and
    -not [string]::IsNullOrWhiteSpace($env:LAKONA_TOOL_SERIALIZER)) {
    $Serializer = $env:LAKONA_TOOL_SERIALIZER
}

$transportLabel = $Transport.Substring(0, 1).ToUpperInvariant() + $Transport.Substring(1)
$serializerLabel = $Serializer.Substring(0, 1).ToUpperInvariant() + $Serializer.Substring(1)
$projectName = "LakonaGodot${transportLabel}${serializerLabel}"
$projectDir = Join-Path $generatedRoot $projectName
$clientDir = Join-Path $projectDir "Client"
$serverSolution = Join-Path $projectDir "Server/Server.slnx"
$serverProject = Join-Path $projectDir "Server/App/Server.App.csproj"
$clientLog = Join-Path $logDir "client.log"
$godotStdoutLog = Join-Path $logDir "godot.stdout.log"
$godotStderrLog = Join-Path $logDir "godot.stderr.log"
$clusterPeers = '[{"Id":"godot-gateway","Endpoint":"tcp://127.0.0.1:21001"},{"Id":"godot-world-a","Endpoint":"tcp://127.0.0.1:21002"},{"Id":"godot-world-b","Endpoint":"tcp://127.0.0.1:21003"}]'
$startedProcesses = [System.Collections.Generic.List[object]]::new()

function Assert-ChildPath {
    param([Parameter(Mandatory)][string]$Path)

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $rootPrefix = $rootDir.TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    if (-not $fullPath.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to modify a path outside the repository: $fullPath"
    }
}

function Invoke-DotNet {
    param([Parameter(Mandatory)][string[]]$Arguments)

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

function Start-LoggedProcess {
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [Parameter(Mandatory)][string[]]$Arguments,
        [Parameter(Mandatory)][string]$StandardOutputPath,
        [Parameter(Mandatory)][string]$StandardErrorPath,
        [hashtable]$Environment = @{}
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FilePath
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    foreach ($argument in $Arguments) {
        [void]$startInfo.ArgumentList.Add($argument)
    }
    foreach ($entry in $Environment.GetEnumerator()) {
        $startInfo.Environment[$entry.Key] = [string]$entry.Value
    }

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    if (-not $process.Start()) {
        throw "Failed to start $FilePath."
    }

    $stdout = [System.IO.FileStream]::new(
        $StandardOutputPath,
        [System.IO.FileMode]::Create,
        [System.IO.FileAccess]::Write,
        [System.IO.FileShare]::ReadWrite)
    $stderr = [System.IO.FileStream]::new(
        $StandardErrorPath,
        [System.IO.FileMode]::Create,
        [System.IO.FileAccess]::Write,
        [System.IO.FileShare]::ReadWrite)
    $record = [pscustomobject]@{
        Process = $process
        StandardOutput = $stdout
        StandardError = $stderr
        StandardOutputCopy = $process.StandardOutput.BaseStream.CopyToAsync($stdout)
        StandardErrorCopy = $process.StandardError.BaseStream.CopyToAsync($stderr)
    }
    $startedProcesses.Add($record)
    return $record
}

function Test-ProcessRunning {
    param([Parameter(Mandatory)]$Record)

    $Record.Process.Refresh()
    return -not $Record.Process.HasExited
}

function Stop-LoggedProcess {
    param([Parameter(Mandatory)]$Record)

    try {
        if (Test-ProcessRunning $Record) {
            $Record.Process.Kill($true)
        }
        [void]$Record.Process.WaitForExit(10000)
        [void][System.Threading.Tasks.Task]::WaitAll(
            @($Record.StandardOutputCopy, $Record.StandardErrorCopy),
            10000)
    }
    catch {
        Write-Warning "Failed to stop process $($Record.Process.Id): $($_.Exception.Message)"
    }
    finally {
        $Record.StandardOutput.Dispose()
        $Record.StandardError.Dispose()
        $Record.Process.Dispose()
    }
}

function Show-Logs {
    Get-ChildItem -Path $logDir -File -ErrorAction SilentlyContinue |
        Sort-Object Name |
        ForEach-Object {
            Write-Host "===== $($_.FullName) =====" -ForegroundColor DarkYellow
            Get-Content -LiteralPath $_.FullName -ErrorAction SilentlyContinue
        }
}

function Wait-ServerReady {
    param(
        [Parameter(Mandatory)][int]$ManagementPort,
        [Parameter(Mandatory)]$ProcessRecord,
        [Parameter(Mandatory)][string]$ServerName
    )

    $readinessUrl = "http://127.0.0.1:$ManagementPort/_lakona/health/ready"
    $lastObservation = "No HTTP response was received."
    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        try {
            $response = Invoke-WebRequest -Uri $readinessUrl -SkipHttpErrorCheck -TimeoutSec 2
            $lastObservation = "HTTP $([int]$response.StatusCode): $($response.Content)"
            if ([int]$response.StatusCode -eq 200) {
                return
            }
        }
        catch {
            $lastObservation = $_.Exception.Message
        }

        if (-not (Test-ProcessRunning $ProcessRecord)) {
            throw "$ServerName exited before application readiness. Exit code: $($ProcessRecord.Process.ExitCode). Last readiness result: $lastObservation"
        }
        Start-Sleep -Seconds 1
    }

    throw "Timed out waiting for application readiness at $readinessUrl. Last readiness result: $lastObservation"
}

function Start-ClusterNode {
    param(
        [Parameter(Mandatory)][string]$NodeId,
        [Parameter(Mandatory)][string]$ActorHosts,
        [Parameter(Mandatory)][int]$ClientPort,
        [Parameter(Mandatory)][int]$ManagementPort,
        [Parameter(Mandatory)][int]$ClusterPort
    )

    Write-Host "Starting $NodeId (client=$ClientPort, management=$ManagementPort, cluster=$ClusterPort)"
    return Start-LoggedProcess `
        -FilePath "dotnet" `
        -Arguments @("run", "--project", $serverProject, "-c", "Release", "--no-build") `
        -StandardOutputPath (Join-Path $logDir "server.$NodeId.stdout.log") `
        -StandardErrorPath (Join-Path $logDir "server.$NodeId.stderr.log") `
        -Environment @{
            "LAKONA__Node__Id" = $NodeId
            "LAKONA__ActorHosts" = $ActorHosts
            "LAKONA__Cluster__Endpoint" = "tcp://127.0.0.1:$ClusterPort"
            "LAKONA__Cluster__Peers" = $clusterPeers
            "LAKONA__Endpoints__0__Host" = "127.0.0.1"
            "LAKONA__Endpoints__0__Port" = $ClientPort
            "LAKONA__Management__Http__Host" = "127.0.0.1"
            "LAKONA__Management__Http__Port" = $ManagementPort
            "LAKONA__Health__ClusterDiagnosticsEnabled" = "true"
            "LAKONA__Observability__Logging__Categories__Lakona.Game.Server.Hosting.ReplicatedClusterMembershipHostedService" = "Debug"
            "LAKONA__Observability__Logging__Categories__Lakona.Rpc.Server.Request" = "Information"
        }
}

function Get-ClusterSnapshot {
    param([Parameter(Mandatory)][int]$ManagementPort)

    $response = Invoke-WebRequest `
        -Uri "http://127.0.0.1:$ManagementPort/_lakona/health/cluster" `
        -SkipHttpErrorCheck `
        -TimeoutSec 2
    if ([int]$response.StatusCode -ne 200) {
        throw "Cluster diagnostics returned HTTP $([int]$response.StatusCode): $($response.Content)"
    }
    return $response.Content | ConvertFrom-Json
}

function Wait-ThreeNodeCluster {
    param([Parameter(Mandatory)][object[]]$Servers)

    $lastObservation = "No cluster diagnostics response was received."
    for ($attempt = 0; $attempt -lt 90; $attempt++) {
        try {
            $snapshots = @(20080, 20081, 20082 | ForEach-Object { Get-ClusterSnapshot $_ })
            $clusterIds = @($snapshots | ForEach-Object { $_.cluster } | Select-Object -Unique)
            $allReady = $true
            foreach ($snapshot in $snapshots) {
                $readyMembers = @($snapshot.members | Where-Object { $_.state -eq "ready" }).Count
                if ($readyMembers -ne 3) {
                    $allReady = $false
                }
            }
            $lastObservation = $snapshots | ConvertTo-Json -Depth 5 -Compress
            if ($clusterIds.Count -eq 1 -and $allReady) {
                Write-Host "Three-node cluster is Ready (cluster=$($clusterIds[0]))."
                return
            }
        }
        catch {
            $lastObservation = $_.Exception.Message
        }

        foreach ($server in $Servers) {
            if (-not (Test-ProcessRunning $server)) {
                throw "A cluster node exited before membership became Ready. Last cluster result: $lastObservation"
            }
        }
        Start-Sleep -Seconds 1
    }

    throw "Timed out waiting for three Ready nodes in one cluster. Last cluster result: $lastObservation"
}

function Resolve-SingleProject {
    param(
        [Parameter(Mandatory)][string]$SearchDirectory,
        [Parameter(Mandatory)][string]$Label
    )

    if (-not (Test-Path -LiteralPath $SearchDirectory -PathType Container)) {
        throw "$Label directory does not exist: $SearchDirectory"
    }
    $projects = @(Get-ChildItem -LiteralPath $SearchDirectory -File -Filter "*.csproj" | Sort-Object FullName)
    if ($projects.Count -ne 1) {
        throw "Expected one $Label project in $SearchDirectory; found $($projects.Count)."
    }
    return $projects[0].FullName
}

function Resolve-GodotMainScene {
    $projectFile = Join-Path $clientDir "project.godot"
    if (-not (Test-Path -LiteralPath $projectFile -PathType Leaf)) {
        throw "Godot project file not found: $projectFile"
    }
    $match = Select-String -LiteralPath $projectFile -Pattern '^\s*run/main_scene\s*=\s*"([^"]+)"' |
        Select-Object -First 1
    if ($null -eq $match) {
        throw "Godot project does not declare application run/main_scene: $projectFile"
    }
    $scene = $match.Matches[0].Groups[1].Value
    if (-not $scene.StartsWith("res://", [StringComparison]::Ordinal)) {
        throw "Unsupported Godot main scene path: $scene"
    }
    $sceneFile = Join-Path $clientDir $scene.Substring("res://".Length)
    if (-not (Test-Path -LiteralPath $sceneFile -PathType Leaf)) {
        throw "Godot main scene does not exist: $scene ($sceneFile)"
    }
    return $scene
}

if ([string]::IsNullOrWhiteSpace($GodotBin) -or
    -not (Test-Path -LiteralPath $GodotBin -PathType Leaf)) {
    throw "GodotBin must point to a Godot Mono executable. Pass -GodotBin or set GODOT_BIN."
}
if ([string]::IsNullOrWhiteSpace($GodotNupkgs) -or
    -not (Test-Path -LiteralPath $GodotNupkgs -PathType Container)) {
    throw "GodotNupkgs must point to GodotSharp/Tools/nupkgs. Pass -GodotNupkgs or set GODOT_NUPKGS."
}

Assert-ChildPath $workDir
Assert-ChildPath $localFeed
try {
    Remove-Item -LiteralPath $workDir -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $localFeed -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path $generatedRoot, $logDir, $localFeed, $packageCache | Out-Null
    $env:NUGET_PACKAGES = $packageCache
    Write-Host "Using clean isolated NuGet package cache: $packageCache"

    $nugetConfig = @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="$localFeed" />
    <add key="godot-local" value="$GodotNupkgs" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
"@
    Set-Content -LiteralPath $ciNuGetConfig -Value $nugetConfig -Encoding utf8

    Write-Host "Packing local Lakona packages into $localFeed"
    $packageProjects = @(
        "src/Lakona.Rpc.Core/Lakona.Rpc.Core.csproj",
        "src/Lakona.Rpc.Client/Lakona.Rpc.Client.csproj",
        "src/Lakona.Rpc.Server/Lakona.Rpc.Server.csproj",
        "src/Lakona.Rpc.Transport.WebSocket/Lakona.Rpc.Transport.WebSocket.csproj",
        "src/Lakona.Rpc.Transport.Tcp/Lakona.Rpc.Transport.Tcp.csproj",
        "src/Lakona.Rpc.Transport.Kcp/Lakona.Rpc.Transport.Kcp.csproj",
        "src/Lakona.Rpc.Serializer.Json/Lakona.Rpc.Serializer.Json.csproj",
        "src/Lakona.Rpc.Serializer.MemoryPack/Lakona.Rpc.Serializer.MemoryPack.csproj",
        "src/Lakona.Game.Abstractions/Lakona.Game.Abstractions.csproj",
        "src/Lakona.Game.Client/Lakona.Game.Client.csproj",
        "src/Lakona.Game.Server/Lakona.Game.Server.csproj",
        "src/Lakona.Tool/Lakona.Tool.csproj"
    )
    foreach ($packageProject in $packageProjects) {
        Invoke-DotNet @("pack", (Join-Path $rootDir $packageProject), "-c", "Release", "-o", $localFeed, "--nologo")
    }

    Write-Host "Generating Lakona Godot project at $projectDir ($Transport + $Serializer)"
    Invoke-DotNet @(
        "run", "--project", (Join-Path $rootDir "src/Lakona.Tool/Lakona.Tool.csproj"), "--",
        "new", "--name", $projectName, "--output", $generatedRoot,
        "--client-engine", "godot", "--transport", $Transport, "--serializer", $Serializer
    )

    $clientProject = Resolve-SingleProject $clientDir "Godot client"
    $godotMainScene = Resolve-GodotMainScene
    Write-Host "Using generated Godot client project: $clientProject"
    Write-Host "Using generated Godot main scene: $godotMainScene"

    Write-Host "Restoring and building generated server solution"
    Invoke-DotNet @("restore", $serverSolution, "--configfile", $ciNuGetConfig)
    Invoke-DotNet @("build", $serverSolution, "-c", "Release", "--no-restore")

    Write-Host "Restoring and building generated Godot client"
    Invoke-DotNet @("restore", $clientProject, "--configfile", $ciNuGetConfig)
    Invoke-DotNet @("build", $clientProject, "-c", "Debug", "--no-restore")

    Write-Host "Starting generated three-node server cluster"
    $gateway = Start-ClusterNode "godot-gateway" "[]" 20000 20080 21001
    $worldA = Start-ClusterNode "godot-world-a" '["gameWorld"]' 20001 20081 21002
    $worldB = Start-ClusterNode "godot-world-b" '["gameWorld"]' 20002 20082 21003
    $servers = @($gateway, $worldA, $worldB)

    Wait-ServerReady 20080 $gateway "Gateway server"
    Wait-ServerReady 20081 $worldA "World-a server"
    Wait-ServerReady 20082 $worldB "World-b server"
    Wait-ThreeNodeCluster $servers

    Write-Host "Running generated Godot client headless"
    $smokeName = "godot-$($Transport.Substring(0, 3))-$($Serializer.Substring(0, 3))"
    $godot = Start-LoggedProcess `
        -FilePath $GodotBin `
        -Arguments @(
            "--headless", "--path", $clientDir, "--scene", $godotMainScene,
            "--log-file", $clientLog, "--verbose", "--no-header"
        ) `
        -StandardOutputPath $godotStdoutLog `
        -StandardErrorPath $godotStderrLog `
        -Environment @{
            "LAKONA_GODOT_SMOKE" = "1"
            "LAKONA_GODOT_SMOKE_NAME" = $smokeName
        }

    $verified = $false
    for ($attempt = 0; $attempt -lt 90; $attempt++) {
        $clientText = @($godotStdoutLog, $godotStderrLog, $clientLog |
            Where-Object { Test-Path -LiteralPath $_ } |
            ForEach-Object { Get-Content -Raw -LiteralPath $_ -ErrorAction SilentlyContinue }) -join "`n"
        if ($clientText.Contains("Request failed:", [StringComparison]::Ordinal) -or
            $clientText.Contains("Connect failed:", [StringComparison]::Ordinal)) {
            throw "Godot client reported a network failure."
        }
        if ($clientText.Contains("Arena smoke ok:", [StringComparison]::Ordinal)) {
            $verified = $true
            break
        }
        if (-not (Test-ProcessRunning $godot)) {
            throw "Godot exited before producing a successful arena smoke log. Exit code: $($godot.Process.ExitCode)"
        }
        Start-Sleep -Seconds 1
    }
    if (-not $verified) {
        throw "Timed out waiting for successful arena smoke login from generated Godot client."
    }

    Write-Host "Lakona Tool Godot $Transport + $Serializer verification passed." -ForegroundColor Green
}
catch {
    Write-Host $_ -ForegroundColor Red
    Show-Logs
    throw
}
finally {
    for ($index = $startedProcesses.Count - 1; $index -ge 0; $index--) {
        Stop-LoggedProcess $startedProcesses[$index]
    }
}
