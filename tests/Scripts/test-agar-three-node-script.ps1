#Requires -Version 7.0
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$target = Join-Path $repoRoot 'scripts/game/ci/test-agar-three-node.ps1'
$tokens = $null
$parseErrors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile($target, [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count -ne 0) { throw "Could not parse $target" }
foreach ($name in @('Wait-Until', 'Run-UnityPlayModeTest', 'Get-DockerPublishedPorts', 'Assert-RequiredPortsAvailable', 'Enter-TestRunLock')) {
    $definition = $ast.Find({ param($node)
        $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name
    }, $true)
    if ($null -eq $definition) { throw "Missing function $name" }
    Invoke-Expression $definition.Extent.Text
}

# Exercise the production traffic wait without launching Unity or Docker.
function Start-Process {
    $fake = [pscustomobject]@{ Id = 123; HasExited = $true; ExitCode = $script:unityExitCode }
    $fake | Add-Member -MemberType ScriptMethod -Name Refresh -Value { }
    return $fake
}
function Stop-Process { $script:stopRequested = $true }
function Get-RemainingSeconds { return 2 }

$clientRoot = $repoRoot
$TestFilter = 'test'
$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ('agar-script-contract-' + [guid]::NewGuid().ToString('N'))
$topologyReady = Join-Path $tempRoot 'ready'
$topologyRelease = Join-Path $tempRoot 'release'
$testResults = Join-Path $tempRoot 'results.xml'
$unityLog = Join-Path $tempRoot 'unity.log'
$lifecycleReport = Join-Path $tempRoot 'lifecycle.json'
foreach ($script:unityExitCode in @(1, 0)) {
    $script:stopRequested = $false
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $failure = $null
    try {
        Run-UnityPlayModeTest -UnityExecutable 'fake-unity' -Timeout 2 -DuringTraffic {
            throw 'Topology changes must not run after Unity exits.'
        }
    }
    catch { $failure = $_.Exception.Message }
    if ($failure -notlike "Unity exited with code $script:unityExitCode before live game traffic was ready.*") {
        throw "Expected immediate Unity exit failure; got: $failure"
    }
    if ($watch.Elapsed.TotalSeconds -ge 2 -or -not $script:stopRequested) {
        throw 'Exited Unity process was not handled promptly.'
    }
}
# Exercise port ownership through structured Docker inspection, including IPv6
# duplicate bindings and mixed protocols, without modifying real containers.
function docker {
    $global:LASTEXITCODE = $script:dockerExitCode
    if ($args[0] -eq 'ps') {
        if ($script:containers.Count -gt 0) { 'test-container-id' }
    }
    elseif ($args[0] -eq 'inspect') {
        ConvertTo-Json -InputObject @($script:containers) -Depth 12
    }
    else { throw 'Unexpected Docker command in port test.' }
}
function Test-TcpPortFree { param([int]$Port) return $Port -ne $script:busyTcpPort }
function Test-UdpPortFree { param([int]$Port) return $Port -ne $script:busyUdpPort }
function Assert-PortFailure {
    param([string]$Expected, [switch]$AllowTargetProject)
    $message = ''
    try { Assert-RequiredPortsAvailable -AllowTargetProject:$AllowTargetProject }
    catch { $message = $_.Exception.Message }
    if ($message -notlike $Expected) { throw "Expected '$Expected', got '$message'." }
}
$ProjectName = 'target-project'
$script:dockerExitCode = 0
$script:containers = @()
$script:busyTcpPort = 0
$script:busyUdpPort = 0
Assert-RequiredPortsAvailable
Assert-RequiredPortsAvailable -AllowTargetProject

$script:containers = @([pscustomobject]@{
    Name = '/retained-gateway'
    Config = @{ Labels = @{ 'com.docker.compose.project' = $ProjectName; 'com.docker.compose.service' = 'gateway-1' } }
    NetworkSettings = @{ Ports = @{ '20000/tcp' = @(
        @{ HostIp = '0.0.0.0'; HostPort = '20000' }
        @{ HostIp = '::'; HostPort = '20000' }
    ) } }
})
$script:busyTcpPort = 20000
Assert-RequiredPortsAvailable -AllowTargetProject
Assert-PortFailure '*20000/tcp*outside the expected target binding*'
$script:containers[0].Config.Labels['com.docker.compose.project'] = 'another-project'
Assert-PortFailure '*outside the expected target binding*' -AllowTargetProject
$script:containers[0].Config.Labels['com.docker.compose.project'] = $ProjectName
$script:containers[0].Config.Labels['com.docker.compose.service'] = 'battle-1'
Assert-PortFailure '*outside the expected target binding*' -AllowTargetProject
$script:containers[0].Config.Labels['com.docker.compose.service'] = 'gateway-1'
$script:containers[0].NetworkSettings.Ports = @{ '12345/tcp' = @(@{ HostPort = '20000' }) }
Assert-PortFailure '*outside the expected target binding*' -AllowTargetProject
# A UDP mapping must never authorize a TCP listener using the same port number.
$script:containers[0].NetworkSettings.Ports = @{ '20000/udp' = @(@{ HostPort = '20000' }) }
Assert-PortFailure '*20000/tcp*non-target process*' -AllowTargetProject
$script:containers = @()
$script:busyTcpPort = 0
$script:busyUdpPort = 20001
Assert-PortFailure '*20001/udp*non-target process*' -AllowTargetProject
$script:dockerExitCode = 1
Assert-PortFailure '*Could not inspect running Docker containers*' -AllowTargetProject

# A separate process must fail immediately while the lock is held, then
# acquire it successfully after release, independent of Compose project name.
$lockDefinition = $ast.Find({ param($node)
    $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Enter-TestRunLock'
}, $true).Extent.Text
$lockName = 'Lakona.AgarThreeNodeE2E.Tests.' + [guid]::NewGuid().ToString('N')
$lock = Enter-TestRunLock -Name $lockName
try {
    $childCode = $lockDefinition + "`n`$lockName = '$lockName'" + @'

try { $lock = Enter-TestRunLock -Name $lockName; $lock.ReleaseMutex(); $lock.Dispose(); exit 1 }
catch { if ($_.Exception.Message -like 'Another Agar three-node test is running.*') { exit 0 }; throw }
'@
    & pwsh -NoProfile -EncodedCommand ([Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($childCode)))
    if ($LASTEXITCODE -ne 0) { throw 'Concurrent run was not rejected.' }
}
finally { $lock.ReleaseMutex(); $lock.Dispose() }
$childCode = $lockDefinition + "`n`$lock = Enter-TestRunLock -Name '$lockName'; `$lock.ReleaseMutex(); `$lock.Dispose()"
& pwsh -NoProfile -EncodedCommand ([Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($childCode)))
if ($LASTEXITCODE -ne 0) { throw 'Run lock was not released.' }

# Exercise the actual startup orchestration with a retained target environment.
# Stop at Compose startup so these cases never launch containers or Unity.
$mainTry = $ast.EndBlock.Statements | Where-Object {
    $_ -is [System.Management.Automation.Language.TryStatementAst] -and
    $_.Body.Extent.Text.Contains('Write-Banner "Preflight"')
} | Select-Object -First 1
if ($null -eq $mainTry) { throw 'Could not locate startup orchestration.' }
$startupStatements = @()
foreach ($statement in $mainTry.Body.Statements) {
    $startupStatements += $statement.Extent.Text
    if ($statement.Extent.Text.StartsWith('Invoke-ComposeStartup ')) { break }
}
function Write-OverrideComposeFile { }
function Write-Banner { }
function Resolve-UnityExecutable { return 'fake-unity' }
function Test-Command { return $true }
function docker {
    $global:LASTEXITCODE = 0
    if ($args -contains 'down') {
        $script:startupEvents.Add('down')
        $script:targetRunning = $false
    }
}
function Assert-RequiredPortsAvailable {
    param([switch]$AllowTargetProject)
    $script:startupEvents.Add("ports:$([bool]$AllowTargetProject)")
    if ($script:foreignPortOccupied -or ($script:targetRunning -and -not $AllowTargetProject)) {
        throw 'Port conflict.'
    }
}
function Invoke-ComposeStartup { $script:startupEvents.Add('up'); throw 'startup-observed' }
$artifactRoot = $tempRoot
$composeFile = $target
$UnityPath = 'fake-unity'
$TimeoutSeconds = 600
$SkipBuild = $true
try {
    foreach ($case in @(
        @{ Reuse = $false; Foreign = $false; Expected = 'ports:True,down,ports:False,up'; Error = 'startup-observed' }
        @{ Reuse = $true; Foreign = $false; Expected = 'ports:True,up'; Error = 'startup-observed' }
        @{ Reuse = $false; Foreign = $true; Expected = 'ports:True'; Error = 'Port conflict.' }
    )) {
        $ReuseEnvironment = $case.Reuse
        $script:foreignPortOccupied = $case.Foreign
        $script:targetRunning = $true
        $script:startupEvents = [Collections.Generic.List[string]]::new()
        $failure = ''
        try { Invoke-Expression ($startupStatements -join "`n") }
        catch { $failure = $_.Exception.Message }
        if ($failure -ne $case.Error -or ($script:startupEvents -join ',') -ne $case.Expected) {
            throw "Unexpected startup behavior: $failure; $($script:startupEvents -join ',')"
        }
    }
}
finally { Remove-Item -LiteralPath $tempRoot -Force }
Write-Host 'Agar three-node script contract: PASS'
