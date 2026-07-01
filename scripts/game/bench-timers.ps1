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

function Assert-PositiveInteger {
    param(
        [string]$Name,
        [int]$Value
    )

    if ($Value -le 0) {
        throw "$Name must be a positive integer."
    }
}

function Assert-PositiveIntegerArray {
    param(
        [string]$Name,
        [int[]]$Values
    )

    foreach ($value in $Values) {
        Assert-PositiveInteger -Name $Name -Value $value
    }
}

function Assert-PeriodsWithinDuration {
    param(
        [int[]]$Periods,
        [int]$Duration
    )

    foreach ($period in $Periods) {
        if ($period -gt $Duration) {
            throw "PeriodMs value $period must be less than or equal to DurationMs value $Duration."
        }
    }
}

$repoRoot = Resolve-Path (Join-Path (Join-Path $PSScriptRoot '..') '..')
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
    if ($PSBoundParameters.ContainsKey('TimerCounts')) {
        Assert-PositiveIntegerArray -Name 'TimerCounts' -Values $TimerCounts
    }

    if ($PSBoundParameters.ContainsKey('PeriodMs')) {
        Assert-PositiveIntegerArray -Name 'PeriodMs' -Values $PeriodMs
    }

    if ($PSBoundParameters.ContainsKey('DurationMs')) {
        Assert-PositiveInteger -Name 'DurationMs' -Value $DurationMs
    }

    if ($PSBoundParameters.ContainsKey('MaxWorkers')) {
        Assert-PositiveInteger -Name 'MaxWorkers' -Value $MaxWorkers
    }

    if ($PSBoundParameters.ContainsKey('QueueCapacity')) {
        Assert-PositiveInteger -Name 'QueueCapacity' -Value $QueueCapacity
    }

    if ($PSBoundParameters.ContainsKey('PeriodMs')) {
        $effectiveDurationMs = if ($PSBoundParameters.ContainsKey('DurationMs')) { $DurationMs } else { 2000 }
        Assert-PeriodsWithinDuration -Periods $PeriodMs -Duration $effectiveDurationMs
    }

    if ($Smoke) {
        $env:LAKONA_TIMER_BENCHMARK_SMOKE = 'true'
    }
    else {
        $env:LAKONA_TIMER_BENCHMARK_SMOKE = 'false'
    }

    if ($PSBoundParameters.ContainsKey('TimerCounts')) {
        $env:LAKONA_TIMER_BENCHMARK_TIMER_COUNTS = $TimerCounts -join ','
    }

    if ($PSBoundParameters.ContainsKey('PeriodMs')) {
        $env:LAKONA_TIMER_BENCHMARK_PERIOD_MS = $PeriodMs -join ','
    }

    if ($PSBoundParameters.ContainsKey('CallbackCosts')) {
        $env:LAKONA_TIMER_BENCHMARK_CALLBACK_COSTS = $CallbackCosts -join ','
    }

    if ($PSBoundParameters.ContainsKey('DurationMs')) {
        $env:LAKONA_TIMER_BENCHMARK_DURATION_MS = $DurationMs.ToString([Globalization.CultureInfo]::InvariantCulture)
    }

    if ($PSBoundParameters.ContainsKey('MaxWorkers')) {
        $env:LAKONA_TIMER_BENCHMARK_MAX_WORKERS = $MaxWorkers.ToString([Globalization.CultureInfo]::InvariantCulture)
    }

    if ($PSBoundParameters.ContainsKey('QueueCapacity')) {
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
