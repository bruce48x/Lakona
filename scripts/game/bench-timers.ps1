param(
    [switch]$Smoke,
    [int[]]$TimerCounts,
    [int[]]$PeriodMs,
    [string[]]$CallbackCosts,
    [int]$DurationMs,
    [int]$MaxWorkers,
    [int]$QueueCapacity
)

$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$benchmarkEnvNames = @(
    'LAKONA_TIMER_BENCHMARK_SMOKE',
    'LAKONA_TIMER_BENCHMARK_TIMER_COUNTS',
    'LAKONA_TIMER_BENCHMARK_PERIOD_MS',
    'LAKONA_TIMER_BENCHMARK_CALLBACK_COSTS',
    'LAKONA_TIMER_BENCHMARK_DURATION_MS',
    'LAKONA_TIMER_BENCHMARK_MAX_WORKERS',
    'LAKONA_TIMER_BENCHMARK_QUEUE_CAPACITY'
)
$previousEnv = @{}
foreach ($name in $benchmarkEnvNames) {
    $previousEnv[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
}

Push-Location $repoRoot
try {
    if ($Smoke) {
        $env:LAKONA_TIMER_BENCHMARK_SMOKE = 'true'
    }
    else {
        $env:LAKONA_TIMER_BENCHMARK_SMOKE = 'false'
    }

    if ($TimerCounts) {
        $env:LAKONA_TIMER_BENCHMARK_TIMER_COUNTS = $TimerCounts -join ','
    }

    if ($PeriodMs) {
        $env:LAKONA_TIMER_BENCHMARK_PERIOD_MS = $PeriodMs -join ','
    }

    if ($CallbackCosts) {
        $env:LAKONA_TIMER_BENCHMARK_CALLBACK_COSTS = $CallbackCosts -join ','
    }

    if ($DurationMs -gt 0) {
        $env:LAKONA_TIMER_BENCHMARK_DURATION_MS = $DurationMs.ToString([Globalization.CultureInfo]::InvariantCulture)
    }

    if ($MaxWorkers -gt 0) {
        $env:LAKONA_TIMER_BENCHMARK_MAX_WORKERS = $MaxWorkers.ToString([Globalization.CultureInfo]::InvariantCulture)
    }

    if ($QueueCapacity -gt 0) {
        $env:LAKONA_TIMER_BENCHMARK_QUEUE_CAPACITY = $QueueCapacity.ToString([Globalization.CultureInfo]::InvariantCulture)
    }

    $testProject = Join-Path (Join-Path 'tests' 'Lakona.Game.Server.Tests') 'Lakona.Game.Server.Tests.csproj'
    dotnet test $testProject --filter LakonaTimerPerformanceTests --no-restore --logger "console;verbosity=detailed"
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}
finally {
    foreach ($name in $benchmarkEnvNames) {
        if ($null -eq $previousEnv[$name]) {
            Remove-Item -LiteralPath "Env:\$name" -ErrorAction SilentlyContinue
        }
        else {
            Set-Item -LiteralPath "Env:\$name" -Value $previousEnv[$name]
        }
    }

    Pop-Location
}
