#Requires -Version 7.0

<#
.SYNOPSIS
Runs the Unity RPC tester shutdown regressions for WebSocket, TCP and KCP.
.DESCRIPTION
Copies each sample from the working tree into a fresh .tmp/rpc-sample-shutdown
directory, restores its NuGetForUnity dependencies and runs the shared PlayMode
fixture. Requires .NET, an activated Unity editor and access to the package feeds.
The scratch projects, Unity logs and NUnit XML are retained for diagnosis.
.EXAMPLE
pwsh -File scripts/rpc/test-unity-sample-shutdown.ps1 -UnityPath "/path/to/Unity"
.EXAMPLE
pwsh -File scripts/rpc/test-unity-sample-shutdown.ps1 -Sample Rpc.Unity.MemoryPack.Tcp -PackageSource ./artifacts/nuget
#>
[CmdletBinding()]
param(
    [ValidateSet("Rpc.Unity.Json.Websocket", "Rpc.Unity.MemoryPack.Tcp", "Rpc.Unity.MemoryPack.Kcp")]
    [string[]]$Sample = @("Rpc.Unity.Json.Websocket", "Rpc.Unity.MemoryPack.Tcp", "Rpc.Unity.MemoryPack.Kcp"),
    [string]$UnityPath = $env:UNITY_PATH,
    [string]$NuGetForUnityPath,
    [string]$PackageSource,
    [ValidateRange(30, 3600)]
    [int]$TimeoutSeconds = 600
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "../..")).Path
$samplesRoot = Join-Path $repoRoot "samples"
$fixture = "Lakona.SampleTests.TesterShutdownTests"
$sharedTests = @(
    "$fixture.Disable_during_login_releases_the_connection",
    "$fixture.Destroy_during_poll_releases_the_connection",
    "$fixture.Shutdown_during_connect_disposes_after_connect_returns"
)
$fixtureRoot = Join-Path $repoRoot "tests/Unity/RpcSampleShutdown"
$runRoot = Join-Path $repoRoot ".tmp/rpc-sample-shutdown/$([guid]::NewGuid().ToString('N').Substring(0, 12))"

function Invoke-BoundedProcess([string]$Executable, [string[]]$Arguments, [string]$WorkingDirectory) {
    $info = [Diagnostics.ProcessStartInfo]::new($Executable)
    $info.WorkingDirectory = $WorkingDirectory
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    foreach ($argument in $Arguments) { $info.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::Start($info)
    try {
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
            throw "Timed out after $TimeoutSeconds seconds running $Executable in $WorkingDirectory."
        }
        if ($process.ExitCode -ne 0) {
            throw "$Executable exited with code $($process.ExitCode) in $WorkingDirectory."
        }
    }
    finally {
        if (-not $process.HasExited) { $process.Kill($true) }
        $process.Dispose()
    }
}

if (-not $UnityPath -and $IsWindows) {
    $versionFile = Join-Path $samplesRoot "$($Sample[0])/Client/ProjectSettings/ProjectVersion.txt"
    $version = (Select-String -LiteralPath $versionFile '^m_EditorVersion: (.+)$').Matches.Groups[1].Value.Trim()
    $UnityPath = Join-Path $env:ProgramFiles "Unity/Hub/Editor/$version/Editor/Unity.exe"
}
if (-not $UnityPath -or -not (Test-Path -LiteralPath $UnityPath -PathType Leaf)) {
    throw "Set UNITY_PATH or pass -UnityPath with the path to the sample's Unity editor executable."
}
$UnityPath = (Resolve-Path -LiteralPath $UnityPath).Path
if ($PackageSource) { $PackageSource = (Resolve-Path -LiteralPath $PackageSource).Path }
$null = New-Item -Path $runRoot -ItemType Directory
Write-Host "Test artifacts: $runRoot"
if (-not $NuGetForUnityPath) {
    $manifest = Get-Content -Raw -LiteralPath (Join-Path $samplesRoot "$($Sample[0])/Client/Packages/manifest.json") | ConvertFrom-Json
    $cliVersion = $manifest.dependencies.'com.github-glitchenzo.nugetforunity'
    $toolPath = Join-Path $runRoot "tools"
    Invoke-BoundedProcess "dotnet" @("tool", "install", "NuGetForUnity.Cli", "--version", $cliVersion, "--tool-path", $toolPath) $runRoot
    $NuGetForUnityPath = Join-Path $toolPath $(if ($IsWindows) { "nugetforunity.exe" } else { "nugetforunity" })
}
$NuGetForUnityPath = (Resolve-Path -LiteralPath $NuGetForUnityPath).Path

foreach ($name in $Sample) {
    $source = Join-Path $samplesRoot $name
    $copy = Join-Path $runRoot $name
    $client = Join-Path $copy "Client"
    $null = New-Item -ItemType Directory -Path $client, (Join-Path $copy "Shared")
    Get-ChildItem -Force -LiteralPath (Join-Path $source "Shared") |
        Where-Object Name -NotIn @("bin", "obj") |
        Copy-Item -Destination (Join-Path $copy "Shared") -Recurse
    foreach ($directory in @("Assets", "Packages", "ProjectSettings")) {
        Copy-Item -LiteralPath (Join-Path $source "Client/$directory") -Destination $client -Recurse
    }
    Copy-Item -LiteralPath $fixtureRoot -Destination (Join-Path $client "Assets/RpcSampleShutdown") -Recurse
    $expected = $sharedTests
    if ($name -eq "Rpc.Unity.MemoryPack.Tcp") {
        Copy-Item -LiteralPath (Join-Path $repoRoot "tests/Unity/RpcTcpSampleShutdownTests.cs") -Destination (Join-Path $client "Assets/RpcSampleShutdown")
        $expected += "$fixture.Disable_during_tcp_login_closes_the_socket_without_a_reply"
    }

    # A relative local feed in the copied NuGet.config would otherwise point into this scratch run.
    $configPath = Join-Path $client "Assets/NuGet.config"
    [xml]$config = Get-Content -Raw -LiteralPath $configPath
    $local = $config.SelectSingleNode("/configuration/packageSources/add[@key='local']")
    $feed = if ($PackageSource) { $PackageSource } else { Join-Path $repoRoot "artifacts" }
    if (Test-Path -LiteralPath $feed -PathType Container) { $local.SetAttribute("value", $feed) }
    else { $null = $local.ParentNode.RemoveChild($local) }
    $config.Save($configPath)
    Invoke-BoundedProcess $NuGetForUnityPath @("restore", $client) $copy
    [xml]$packages = Get-Content -Raw -LiteralPath (Join-Path $client "Assets/packages.config")
    $installed = $packages.packages.package | Where-Object id -Like "Lakona.*" | ForEach-Object { "$($_.id) $($_.version)" }
    Write-Host "$name uses $($installed -join ', ')"

    $results = Join-Path $copy "shutdown-results.xml"
    $editorLog = Join-Path $copy "unity.log"
    Write-Host "Running $name ($editorLog)"
    Invoke-BoundedProcess $UnityPath @("-batchmode", "-nographics", "-projectPath", $client,
        "-runTests", "-testPlatform", "PlayMode", "-testFilter", $fixture,
        "-testResults", $results, "-logFile", $editorLog) $copy
    [xml]$xml = Get-Content -LiteralPath $results -Raw
    $run = $xml.DocumentElement
    $cases = @($xml.SelectNodes("/test-run//test-case"))
    if ($run.result -ne "Passed" -or $run.total -ne "$($expected.Count)" -or $run.passed -ne $run.total -or
        $run.failed -ne "0" -or $run.skipped -ne "0" -or $run.inconclusive -ne "0" -or
        $cases.Count -ne $expected.Count) {
        throw "$name did not complete all shutdown tests. See $results and $editorLog."
    }
    foreach ($testName in $expected) {
        $passed = @($cases | Where-Object { $_.fullname -eq $testName -and $_.result -eq "Passed" })
        if ($passed.Count -ne 1) { throw "Expected one passing result for $testName in $results." }
    }
    Write-Host "$name passed $($cases.Count) shutdown tests."
}
