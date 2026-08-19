#Requires -Version 7.0

[CmdletBinding()]
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
    [int]$StartupIntervalMilliseconds = 100
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

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
$instanceLogRoot = Join-Path $outputRoot "logs"

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
    Write-Host ("Started client {0}/{1}, PID={2}" -f $instance, $InstanceCount, $process.Id)

    if ($StartupIntervalMilliseconds -gt 0 -and $instance -lt $InstanceCount) {
        Start-Sleep -Milliseconds $StartupIntervalMilliseconds
    }
}

Write-Host "Started $($processes.Count) stress clients against ws://${HostName}:${Port}${Path}."
Write-Host "Build output: $buildOutputPath"
Write-Host "Logs: $instanceLogRoot"
Write-Output $processes
