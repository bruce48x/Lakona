#Requires -Version 7.0
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$target = Join-Path $repoRoot 'scripts/game/ci/test-agar-three-node.ps1'
$tokens = $null
$parseErrors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile($target, [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count -ne 0) { throw "Could not parse $target" }
foreach ($name in @('Wait-Until', 'Run-UnityPlayModeTest')) {
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
Write-Host 'Agar three-node script contract: PASS'
