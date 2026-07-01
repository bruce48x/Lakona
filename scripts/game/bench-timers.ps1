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

    dotnet test tests\Lakona.Game.Server.Tests\Lakona.Game.Server.Tests.csproj --filter LakonaTimerPerformanceTests --no-restore --logger "console;verbosity=detailed"
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}
finally {
    Pop-Location
}
