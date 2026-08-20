#Requires -Version 7.0

[CmdletBinding(PositionalBinding = $false)]
param(
    [ValidateRange(1, 1000)]
    [int]$InstanceCount = 10,
    [Alias("Host")]
    [string]$HostName = "",
    [ValidateRange(0, 65535)]
    [int]$Port = 0,
    [string]$Path = "",
    [string]$UnityPath = "",
    [string]$OutputDirectory = "",
    [switch]$SkipBuild,
    [switch]$BuildOnly,
    [Alias("ShowWindows")]
    [switch]$ShowWindow,
    [ValidateRange(0, 60000)]
    [int]$StartupIntervalMilliseconds = 100,
    [ValidateRange(1, 3600)]
    [int]$StatusIntervalSeconds = 5,
    [ValidateRange(5, 3600)]
    [int]$StallTimeoutSeconds = 30,
    [ValidateRange(0, 86400)]
    [int]$DurationSeconds = 0,
    [switch]$Detach,
    [switch]$StopRun,
    [Alias("h")]
    [switch]$Help,
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$ExtraArguments = @()
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$showHelp = $Help -or $ExtraArguments -contains "--help"
if ($showHelp) {
    @"
Agar Unity stress client builder, launcher, and monitor.

USAGE
  ./client-stress.ps1 [options]
  ./client-stress.ps1 --help | -h | -Help

OPTIONS
  -InstanceCount <1-1000>         Client instances to start. Default: 10.
  -Host <name>                    WebSocket host. Default: server appsettings.json.
  -Port <1-65535>                 WebSocket port. Default: server appsettings.json.
  -Path <path>                    WebSocket path. Default: server appsettings.json.
  -UnityPath <path>               Unity executable. Auto-detected when omitted.
  -OutputDirectory <path>         Build and log directory. Default: artifacts/agar-client-stress.
  -SkipBuild                      Reuse the existing platform build.
  -BuildOnly                      Build the client without starting instances.
  -ShowWindow                     Show client windows instead of headless mode.
  -StartupIntervalMilliseconds N  Delay between client starts. Default: 100.
  -StatusIntervalSeconds N        Monitor refresh interval. Default: 5.
  -StallTimeoutSeconds N          Mark a battle stalled after no tick progress. Default: 30.
  -DurationSeconds N              Stop clients after N seconds. Default: 0 (until Ctrl+C).
  -Detach                         Start clients in the background and exit immediately.
  -StopRun                        Stop all running Agar stress clients and exit.
  --help, -h, -Help               Show this help and exit.

EXAMPLES
  ./client-stress.ps1 -InstanceCount 10
  ./client-stress.ps1 -InstanceCount 50 -DurationSeconds 300 -SkipBuild
  ./client-stress.ps1 -Host 10.0.0.20 -Port 20000 -Detach
  ./client-stress.ps1 -StopRun

By default the script monitors all clients. Press Ctrl+C to stop the current run
and all client processes started by it.
"@ | Write-Output
    return
}

if ($ExtraArguments.Count -gt 0) {
    throw "Unknown argument(s): $($ExtraArguments -join ', '). Run with --help, -h, or -Help for usage."
}

function Get-RunningStressClients {
    $processes = @(Get-Process -Name "AgarStressClient", "AgarStressClien", "Client" -ErrorAction SilentlyContinue)
    return @($processes | Where-Object {
        if ($_.ProcessName -ne "Client") {
            return $true
        }

        try {
            return $_.Path -match '[\\/]AgarStressClient\.app[\\/]Contents[\\/]MacOS[\\/]'
        }
        catch {
            return $false
        }
    } | Sort-Object Id -Unique)
}

function Stop-RunningStressClients {
    $clients = @(Get-RunningStressClients)
    if ($clients.Count -eq 0) {
        Write-Host "No running Agar stress clients were found."
        return
    }

    $stoppedCount = 0
    $failures = [System.Collections.Generic.List[string]]::new()
    foreach ($client in $clients) {
        try {
            Stop-Process -Id $client.Id -Force -ErrorAction Stop
            $stoppedCount++
            Write-Host ("Stopped client PID={0}, process={1}" -f $client.Id, $client.ProcessName)
        }
        catch {
            if ($null -eq (Get-Process -Id $client.Id -ErrorAction SilentlyContinue)) {
                continue
            }

            $failures.Add("PID=$($client.Id): $($_.Exception.Message)")
        }
    }

    Write-Host "Stopped $stoppedCount Agar stress client(s)."
    if ($failures.Count -gt 0) {
        throw "Failed to stop $($failures.Count) client(s): $($failures -join '; ')"
    }
}

if ($StopRun) {
    Stop-RunningStressClients
    return
}

$sampleRoot = $PSScriptRoot
$clientRoot = Join-Path $sampleRoot "Client"
$repositoryRoot = (Resolve-Path (Join-Path $sampleRoot "../..")).Path
$appSettingsPath = Join-Path $sampleRoot "Server/App/appsettings.json"
if (-not (Test-Path -LiteralPath $appSettingsPath -PathType Leaf)) {
    throw "Agar server appsettings file was not found: $appSettingsPath"
}

$appSettings = Get-Content -Raw -LiteralPath $appSettingsPath | ConvertFrom-Json
$defaultEndpoint = @($appSettings.Lakona.Endpoints) |
    Where-Object { $_.Transport -eq "websocket" } |
    Select-Object -First 1
if ($null -eq $defaultEndpoint) {
    throw "A websocket client endpoint was not found in: $appSettingsPath"
}

if ([string]::IsNullOrWhiteSpace($HostName)) {
    $HostName = [string]$defaultEndpoint.Host
}
if ($Port -eq 0) {
    $Port = [int]$defaultEndpoint.Port
}
if ([string]::IsNullOrWhiteSpace($Path)) {
    $Path = [string]$defaultEndpoint.Path
}
if ([string]::IsNullOrWhiteSpace($HostName) -or $Port -lt 1 -or $Port -gt 65535 -or [string]::IsNullOrWhiteSpace($Path)) {
    throw "The websocket client endpoint in $appSettingsPath must define Host, Port, and Path."
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repositoryRoot "artifacts/agar-client-stress"
}

$outputRoot = [System.IO.Path]::GetFullPath($OutputDirectory)
$buildPlatform = ""
$buildOutputPath = ""
$executablePath = ""
if ($IsWindows) {
    $buildPlatform = "windows"
    $buildOutputPath = Join-Path $outputRoot "AgarStressClient.exe"
    $executablePath = $buildOutputPath
}
elseif ($IsMacOS) {
    $buildPlatform = "macos"
    $buildOutputPath = Join-Path $outputRoot "AgarStressClient.app"
}
elseif ($IsLinux) {
    $buildPlatform = "linux"
    $buildOutputPath = Join-Path $outputRoot "AgarStressClient"
    $executablePath = $buildOutputPath
}
else {
    throw "client-stress.ps1 supports only Windows, macOS, and Linux."
}

$buildLogPath = Join-Path $outputRoot "build.log"
$runId = "{0}-{1}" -f (Get-Date -Format "yyyyMMdd-HHmmss-fff"), $PID
$instanceLogRoot = Join-Path $outputRoot ("logs/{0}" -f $runId)
$roundFrameCount = 120 * 20

function Get-UnityProjectEditorVersion {
    $versionFile = Join-Path $clientRoot "ProjectSettings/ProjectVersion.txt"
    if (-not (Test-Path -LiteralPath $versionFile)) {
        throw "Unity project version file was not found: $versionFile"
    }

    $versionLine = Get-Content -LiteralPath $versionFile | Where-Object { $_ -like "m_EditorVersion:*" } | Select-Object -First 1
    if ($versionLine -notmatch '^m_EditorVersion:\s*(?<Version>\S+)') {
        throw "Unity editor version could not be read from: $versionFile"
    }

    return $Matches["Version"]
}

function Resolve-UnityExecutable {
    $candidates = [System.Collections.Generic.List[string]]::new()
    if (-not [string]::IsNullOrWhiteSpace($UnityPath)) {
        $candidates.Add($UnityPath)
    }
    if (-not [string]::IsNullOrWhiteSpace($env:UNITY_PATH)) {
        $candidates.Add($env:UNITY_PATH)
    }

    $editorVersion = Get-UnityProjectEditorVersion
    $hubRoot = ""
    $editorRelativePath = ""
    if ($IsWindows) {
        $programFiles = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)
        if (-not [string]::IsNullOrWhiteSpace($programFiles)) {
            $hubRoot = Join-Path $programFiles "Unity/Hub/Editor"
            $editorRelativePath = "Editor/Unity.exe"
            $candidates.Add((Join-Path $programFiles "Unity/Editor/Unity.exe"))
        }
    }
    elseif ($IsMacOS) {
        $hubRoot = "/Applications/Unity/Hub/Editor"
        $editorRelativePath = "Unity.app/Contents/MacOS/Unity"
        $candidates.Add("/Applications/Unity/Unity.app/Contents/MacOS/Unity")
    }
    elseif ($IsLinux) {
        $userProfile = [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)
        if (-not [string]::IsNullOrWhiteSpace($userProfile)) {
            $hubRoot = Join-Path $userProfile "Unity/Hub/Editor"
        }
        $editorRelativePath = "Editor/Unity"
        $candidates.Add("/opt/unity/Editor/Unity")
    }

    if (-not [string]::IsNullOrWhiteSpace($hubRoot)) {
        $candidates.Add((Join-Path (Join-Path $hubRoot $editorVersion) $editorRelativePath))
        if (Test-Path -LiteralPath $hubRoot) {
            Get-ChildItem -LiteralPath $hubRoot -Directory -ErrorAction SilentlyContinue |
                Where-Object { $_.Name -like "2022.3.*" } |
                Sort-Object Name -Descending |
                ForEach-Object { $candidates.Add((Join-Path $_.FullName $editorRelativePath)) }
        }
    }

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }

    throw "Unity executable was not found. Pass -UnityPath or set UNITY_PATH."
}

function Assert-UnityProjectNotOpen {
    $instanceFile = Join-Path $clientRoot "Library/EditorInstance.json"
    if (-not (Test-Path -LiteralPath $instanceFile -PathType Leaf)) {
        return
    }

    try {
        $editorInstance = Get-Content -Raw -LiteralPath $instanceFile | ConvertFrom-Json
        $editorProcess = Get-Process -Id ([int]$editorInstance.process_id) -ErrorAction SilentlyContinue
        if ($null -ne $editorProcess) {
            throw "The Agar Unity project is open in PID $($editorProcess.Id). Close the editor before building the stress client."
        }
    }
    catch [System.Management.Automation.RuntimeException] {
        throw
    }
    catch {
        Write-Warning "Could not inspect the Unity editor instance file: $($_.Exception.Message)"
    }
}

function Resolve-StressClientExecutable {
    if (-not $IsMacOS) {
        return $buildOutputPath
    }

    $macExecutableRoot = Join-Path $buildOutputPath "Contents/MacOS"
    if (-not (Test-Path -LiteralPath $macExecutableRoot -PathType Container)) {
        return (Join-Path $macExecutableRoot "AgarStressClient")
    }

    $macExecutables = @(Get-ChildItem -LiteralPath $macExecutableRoot -File -ErrorAction SilentlyContinue)
    $preferredExecutable = $macExecutables |
        Where-Object { $_.Name -in @("AgarStressClient", "Client") } |
        Select-Object -First 1
    if ($null -ne $preferredExecutable) {
        return $preferredExecutable.FullName
    }
    if ($macExecutables.Count -eq 1) {
        return $macExecutables[0].FullName
    }

    throw "Could not identify the Unity player executable under: $macExecutableRoot"
}

function Get-StressLogSnapshot {
    param([string]$LogPath)

    $state = "Starting"
    $tick = -1
    $leaderboard = "-"
    if (-not (Test-Path -LiteralPath $LogPath -PathType Leaf)) {
        return [pscustomobject]@{ State = $state; Tick = $tick; Leaderboard = $leaderboard }
    }

    foreach ($line in @(Get-Content -LiteralPath $LogPath -Tail 2000 -ErrorAction SilentlyContinue)) {
        if ($line -match '\[Stress\] Starting automated client') {
            $state = "Connecting"
        }
        elseif ($line -match '\[Stress\] Logged in|\[Stress\] Matchmaking requested') {
            $state = "Matchmaking"
        }
        elseif ($line -match 'ApplyWorldState complete tick=(?<Tick>\d+)|WorldState tick=(?<Tick>\d+)') {
            $tick = [int]$Matches["Tick"]
            $state = "Battle"
        }
        elseif ($line -match '\[Stress\] Settlement submitted') {
            $state = "Settlement"
        }
        elseif ($line -match '\[Stress\] Automated login failed|Start matchmaking failed|Match result submission failed') {
            $state = "Error"
        }

        if ($line -match '\[Stress\] Leaderboard refreshed entries=(?<Entries>\d+), localRank=(?<Rank>\d+), victoryPoints=(?<Points>\d+), wins=(?<Wins>\d+)') {
            $leaderboard = "rank=$($Matches['Rank']) points=$($Matches['Points']) wins=$($Matches['Wins'])"
        }
        elseif ($line -match 'Leaderboard refresh failed') {
            $leaderboard = "error"
        }
    }

    return [pscustomobject]@{ State = $state; Tick = $tick; Leaderboard = $leaderboard }
}

function Format-StressDuration {
    param([TimeSpan]$Duration)

    return "{0:00}:{1:00}:{2:00}" -f [Math]::Floor($Duration.TotalHours), $Duration.Minutes, $Duration.Seconds
}

function Show-StressStatus {
    param([System.Collections.Generic.List[object]]$Clients)

    $now = [DateTime]::UtcNow
    $rows = foreach ($client in $Clients) {
        $isRunning = $false
        try {
            $client.Process.Refresh()
            $isRunning = -not $client.Process.HasExited
        }
        catch {
        }

        $snapshot = Get-StressLogSnapshot -LogPath $client.LogPath
        if ($snapshot.Tick -ge 0 -and $snapshot.Tick -ne $client.LastTick) {
            if ($client.LastTick -ge 0 -and $snapshot.Tick -lt $client.LastTick) {
                $client.Round++
            }
            $client.LastTick = $snapshot.Tick
            $client.LastTickChangedAt = $now
        }
        if ($snapshot.Leaderboard -ne "-") {
            $client.LastLeaderboard = $snapshot.Leaderboard
        }

        $state = $snapshot.State
        if (-not $isRunning) {
            $state = "Exited"
        }
        elseif ($state -eq "Battle" -and
                $snapshot.Tick -ge 0 -and
                ($now - $client.LastTickChangedAt).TotalSeconds -ge $StallTimeoutSeconds) {
            $state = "Stalled"
        }

        $progress = if ($snapshot.Tick -ge 0) {
            "{0}/{1} ({2:P0})" -f $snapshot.Tick, $roundFrameCount, [Math]::Min(1, $snapshot.Tick / $roundFrameCount)
        }
        else {
            "-"
        }
        $lastLog = if (Test-Path -LiteralPath $client.LogPath -PathType Leaf) {
            (Get-Item -LiteralPath $client.LogPath).LastWriteTime.ToString("HH:mm:ss")
        }
        else {
            "-"
        }

        [pscustomobject]@{
            Client = $client.Name
            PID = $client.Process.Id
            Uptime = Format-StressDuration ($now - $client.StartedAt)
            State = $state
            Round = $client.Round
            Tick = $snapshot.Tick
            Progress = $progress
            Leaderboard = $client.LastLeaderboard
            LastLog = $lastLog
            Running = $isRunning
        }
    }

    Write-Host ""
    Write-Host ("[{0}] Stress client status" -f (Get-Date -Format "yyyy-MM-dd HH:mm:ss"))
    $rows |
        Select-Object Client, PID, Uptime, State, Round, Tick, Progress, Leaderboard, LastLog |
        Format-Table -AutoSize |
        Out-Host
    return @($rows)
}

if (-not $SkipBuild) {
    New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null
    Assert-UnityProjectNotOpen
    $unity = Resolve-UnityExecutable
    Write-Host "Building $buildPlatform stress client with $unity"
    $buildStartInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $buildStartInfo.FileName = $unity
    $buildStartInfo.UseShellExecute = $false
    @(
        "-batchmode", "-nographics", "-quit",
        "-projectPath", $clientRoot,
        "-executeMethod", "AgarStressBuild.BuildClient",
        "-buildPlatform", $buildPlatform,
        "-buildOutput", $buildOutputPath,
        "-logFile", $buildLogPath
    ) | ForEach-Object { $buildStartInfo.ArgumentList.Add($_) }

    $buildProcess = [System.Diagnostics.Process]::Start($buildStartInfo)
    if ($null -eq $buildProcess) {
        throw "Unity build process could not be started."
    }

    $buildProcess.WaitForExit()
    if ($buildProcess.ExitCode -ne 0) {
        throw "Unity build failed with exit code $($buildProcess.ExitCode). See $buildLogPath"
    }
}

$executablePath = Resolve-StressClientExecutable
if (-not (Test-Path -LiteralPath $executablePath -PathType Leaf)) {
    throw "Stress client executable was not found for ${buildPlatform}: $executablePath. Remove -SkipBuild or set -OutputDirectory."
}

if ($BuildOnly) {
    Write-Host "Stress client built: $buildOutputPath"
    return
}

New-Item -ItemType Directory -Force -Path $instanceLogRoot | Out-Null
$processes = [System.Collections.Generic.List[System.Diagnostics.Process]]::new()
$clients = [System.Collections.Generic.List[object]]::new()
for ($instance = 1; $instance -le $InstanceCount; $instance++) {
    $logPath = Join-Path $instanceLogRoot ("client-{0:D4}.log" -f $instance)
    $arguments = @(
        "--stress",
        "--host", $HostName,
        "--port", $Port.ToString([Globalization.CultureInfo]::InvariantCulture),
        "--path", $Path,
        "-logFile", $logPath
    )
    if (-not $ShowWindow) {
        $arguments = @("-batchmode", "-nographics") + $arguments
    }

    $clientStartInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $clientStartInfo.FileName = $executablePath
    $clientStartInfo.WorkingDirectory = $outputRoot
    $clientStartInfo.UseShellExecute = $false
    $arguments | ForEach-Object { $clientStartInfo.ArgumentList.Add($_) }
    $process = [System.Diagnostics.Process]::Start($clientStartInfo)
    if ($null -eq $process) {
        throw "Stress client $instance could not be started."
    }

    $processes.Add($process)
    $clients.Add([pscustomobject]@{
        Name = "client-{0:D4}" -f $instance
        Process = $process
        LogPath = $logPath
        StartedAt = [DateTime]::UtcNow
        Round = 1
        LastTick = -1
        LastTickChangedAt = [DateTime]::UtcNow
        LastLeaderboard = "-"
    })
    Write-Host ("Started client {0}/{1}, PID={2}" -f $instance, $InstanceCount, $process.Id)

    if ($StartupIntervalMilliseconds -gt 0 -and $instance -lt $InstanceCount) {
        Start-Sleep -Milliseconds $StartupIntervalMilliseconds
    }
}

Write-Host "Started $($processes.Count) stress clients against ws://${HostName}:${Port}${Path}."
Write-Host "Build output: $buildOutputPath"
Write-Host "Logs: $instanceLogRoot"
if ($Detach) {
    Write-Host "Clients are detached. Their current process objects follow."
    Write-Output $processes
    return
}

Write-Host "Monitoring clients. Press Ctrl+C to stop this run and its client processes."
if ($DurationSeconds -gt 0) {
    Write-Host "This run will stop automatically after $DurationSeconds seconds."
}

$monitorStartedAt = [DateTime]::UtcNow
try {
    while ($true) {
        $rows = @(Show-StressStatus -Clients $clients)
        if (@($rows | Where-Object Running).Count -eq 0) {
            Write-Host "All stress clients have exited."
            break
        }
        if ($DurationSeconds -gt 0 -and
            ([DateTime]::UtcNow - $monitorStartedAt).TotalSeconds -ge $DurationSeconds) {
            Write-Host "Stress duration reached; stopping clients."
            break
        }

        Start-Sleep -Seconds $StatusIntervalSeconds
    }
}
finally {
    foreach ($client in $clients) {
        try {
            if (-not $client.Process.HasExited) {
                Stop-Process -Id $client.Process.Id -Force -ErrorAction SilentlyContinue
            }
        }
        catch {
        }
    }
}
