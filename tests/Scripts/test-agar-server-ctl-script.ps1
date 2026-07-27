#Requires -Version 5.1

$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptRoot "../..")
$controlScript = Join-Path $repoRoot "samples/Game.Unity.Agar/server-ctl.ps1"
$composeFile = Join-Path $repoRoot "samples/Game.Unity.Agar/docker-compose.yml"

if (-not (Test-Path -LiteralPath $controlScript)) {
    throw "Missing Agar server control script: $controlScript"
}

$scriptContent = Get-Content -Raw -LiteralPath $controlScript
$composeContent = Get-Content -Raw -LiteralPath $composeFile

$requiredScriptFragments = @(
    '[ValidateSet("single", "three")]',
    '[string]$Topology = "three"',
    'start [-Topology single|three]',
    'docker compose --file $composeFile --profile single @Arguments',
    'Get-TopologyServices',
    'Get-OtherTopologyServices',
    '@("postgres", "redis", "single-1")',
    '@("postgres", "redis", "data-1", "gateway-1", "battle-1")',
    'Game.Unity.Agar $Topology topology is ready.'
)

foreach ($fragment in $requiredScriptFragments) {
    if (-not $scriptContent.Contains($fragment)) {
        throw "Expected '$fragment' in $controlScript"
    }
}

$requiredComposeFragments = @(
    'single-1:',
    'profiles:',
    '- single',
    'container_name: lakona-agar-single-1',
    'Lakona__ActorHosts: ''["user","matchmaking","leaderboard","room"]''',
    '"Transport": "websocket"',
    '"Transport": "kcp"'
)

foreach ($fragment in $requiredComposeFragments) {
    if (-not $composeContent.Contains($fragment)) {
        throw "Expected '$fragment' in $composeFile"
    }
}

Write-Host "Game.Unity.Agar server control contract: PASS"
