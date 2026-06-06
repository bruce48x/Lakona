[CmdletBinding()]
param(
    [ValidateSet("Unity.MemoryPack.Tcp", "Unity.Json.Websocket", "Unity.MemoryPack.Kcp", "Godot.MixedTransport")]
    [string]$Sample = "Unity.MemoryPack.Tcp",

    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",

    [ValidateSet("quiet", "minimal", "normal", "detailed", "diagnostic")]
    [string]$Verbosity = "minimal",

    [int]$Port,
    [switch]$Run,
    [switch]$SkipBuild,
    [switch]$NoRestore,
    [switch]$AllowParallel,
    [switch]$DisableBuildServer,
    [switch]$IgnoreFailedSources,

    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$ServerArgs = @()
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot

$sampleConfig = @{
    "Unity.MemoryPack.Tcp" = @{
        Project = "samples/Unity.MemoryPack.Tcp/Server/Server/Server.csproj"
        AssemblyName = "Samples.Server"
        Contracts = "samples/Unity.MemoryPack.Tcp/Client/Packages/com.samples.contracts"
    }
    "Unity.Json.Websocket" = @{
        Project = "samples/Unity.Json.Websocket/Server/Server/Server.csproj"
        AssemblyName = "Samples.Server"
        Contracts = "samples/Unity.Json.Websocket/Client/Packages/com.samples.contracts"
    }
    "Unity.MemoryPack.Kcp" = @{
        Project = "samples/Unity.MemoryPack.Kcp/Server/Server/Server.csproj"
        AssemblyName = "Samples.Server"
        Contracts = "samples/Unity.MemoryPack.Kcp/Client/Packages/com.samples.contracts"
    }
    "Godot.MixedTransport" = @{
        Project = "samples/Godot.MixedTransport/Server/Server/Server.csproj"
        AssemblyName = "Agar.MixedTransport.Server"
        Contracts = "samples/Godot.MixedTransport/Shared/Interfaces"
    }
}

$config = $sampleConfig[$Sample]
if ($null -eq $config) {
    throw "Unsupported sample: $Sample"
}

$projectPath = Join-Path $repoRoot $config.Project
$projectDir = Split-Path -Parent $projectPath
$assemblyName = $config.AssemblyName
$contractsPath = Join-Path $repoRoot $config.Contracts
$targetDllPath = Join-Path $projectDir ("bin/{0}/net10.0/{1}.dll" -f $Configuration, $assemblyName)

foreach ($path in @($projectPath, $contractsPath)) {
    if (-not (Test-Path $path)) {
        throw "Required path not found: $path"
    }
}

$env:DOTNET_CLI_HOME = Join-Path $repoRoot ".dotnet"
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
$env:NUGET_PACKAGES = Join-Path $repoRoot ".nuget\packages"
$env:MSBUILDDISABLENODEREUSE = "1"

foreach ($path in @($env:DOTNET_CLI_HOME, $env:NUGET_PACKAGES)) {
    if (-not (Test-Path $path)) {
        New-Item -ItemType Directory -Path $path | Out-Null
    }
}

function Invoke-DotNet {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Command,

        [string[]]$Arguments = @()
    )

    Write-Host "==> dotnet $Command $($Arguments -join ' ')" -ForegroundColor Cyan
    & dotnet $Command @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $Command failed with exit code $LASTEXITCODE"
    }
}

function Get-MsBuildArgs {
    if ($AllowParallel) {
        return @("-m")
    }

    return @("-m:1", "/nr:false")
}

function Stop-SampleProcesses {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$MatchTerms
    )

    try {
        $processes = @(Get-CimInstance Win32_Process | Where-Object {
            if ([string]::IsNullOrWhiteSpace($_.CommandLine)) {
                return $false
            }

            foreach ($term in $MatchTerms) {
                if (-not [string]::IsNullOrWhiteSpace($term) -and $_.CommandLine.IndexOf($term, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
                    return $true
                }
            }

            return $false
        })
    } catch {
        Write-Warning "Skipping process cleanup: $($_.Exception.Message)"
        return
    }

    foreach ($process in $processes) {
        if ($process.ProcessId -eq $PID) {
            continue
        }

        try {
            Stop-Process -Id $process.ProcessId -Force -ErrorAction Stop
            Write-Host "==> stopped existing process $($process.ProcessId) for $Sample" -ForegroundColor Yellow
        } catch {
            Write-Warning "Failed to stop process $($process.ProcessId): $($_.Exception.Message)"
        }
    }
}

if (-not $SkipBuild) {
    Stop-SampleProcesses -MatchTerms @($projectPath, $targetDllPath, "$assemblyName.dll", "$assemblyName.exe")

    $buildArgs = @($projectPath, "-c", $Configuration, "-v", $Verbosity) + (Get-MsBuildArgs)
    if ($DisableBuildServer) {
        $buildArgs += "--disable-build-servers"
    }
    if ($NoRestore) {
        $buildArgs += "--no-restore"
    } else {
        if (-not $AllowParallel) {
            $buildArgs += "/p:RestoreDisableParallel=true"
        }
        if ($IgnoreFailedSources) {
            $buildArgs += "--ignore-failed-sources"
        }
    }

    Invoke-DotNet -Command "build" -Arguments $buildArgs
}

if ($Run) {
    if ($SkipBuild) {
        Stop-SampleProcesses -MatchTerms @($projectPath, $targetDllPath, "$assemblyName.dll", "$assemblyName.exe")
    }

    if (-not (Test-Path $targetDllPath)) {
        throw "Built server assembly not found: $targetDllPath"
    }

    $runArgs = @($targetDllPath)

    if ($PSBoundParameters.ContainsKey("Port")) {
        $runArgs += $Port.ToString()
    }

    if ($ServerArgs.Count -gt 0) {
        $runArgs += $ServerArgs
    }

    Write-Host "==> dotnet $($runArgs -join ' ')" -ForegroundColor Cyan
    & dotnet @runArgs
    exit $LASTEXITCODE
}
