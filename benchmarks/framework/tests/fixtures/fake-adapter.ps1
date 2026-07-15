param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("prepare", "server", "driver")]
    [string]$Mode,

    [string]$Role,
    [int]$Port,
    [string]$PidFile,
    [string]$CaseFile,
    [string]$ResultFile
)

$ErrorActionPreference = "Stop"
$behavior = $env:FAKE_BENCHMARK_BEHAVIOR
if ([string]::IsNullOrWhiteSpace($behavior)) {
    $behavior = "normal"
}

if ($Mode -eq "prepare") {
    $pidDirectory = Split-Path -Parent $PidFile
    New-Item -ItemType Directory -Force -Path $pidDirectory | Out-Null
    Set-Content -LiteralPath $PidFile -Value $PID -NoNewline
    exit 0
}

if ($Mode -eq "server") {
    $pidDirectory = Split-Path -Parent $PidFile
    New-Item -ItemType Directory -Force -Path $pidDirectory | Out-Null
    Set-Content -LiteralPath $PidFile -Value $PID -NoNewline

    if ($behavior -eq "exit-before-ready") {
        exit 17
    }

    if ($behavior -eq "malformed-ready") {
        Write-Output "not-json"
        while ($true) {
            Start-Sleep -Milliseconds 100
        }
    }

    if ($behavior -eq "never-ready") {
        while ($true) {
            Start-Sleep -Milliseconds 100
        }
    }

    $ready = [ordered]@{
        event = "ready"
        role = $Role
        nodeId = "fake-frontdoor"
        endpoints = [ordered]@{
            client = "tcp://127.0.0.1:$Port"
        }
    }
    Write-Output ($ready | ConvertTo-Json -Compress -Depth 4)
    if ($behavior -eq "duplicate-ready") {
        Write-Output ($ready | ConvertTo-Json -Compress -Depth 4)
    }
    while ($true) {
        Start-Sleep -Milliseconds 100
    }
}

if ($behavior -eq "exit") {
    exit 23
}

if (-not [string]::IsNullOrWhiteSpace($PidFile)) {
    $pidDirectory = Split-Path -Parent $PidFile
    New-Item -ItemType Directory -Force -Path $pidDirectory | Out-Null
    Set-Content -LiteralPath $PidFile -Value $PID -NoNewline
}

if ($behavior -eq "never-completes") {
    while ($true) {
        Start-Sleep -Milliseconds 100
    }
}

$case = Get-Content -Raw -LiteralPath $CaseFile | ConvertFrom-Json
$completed = 4
$result = [ordered]@{
    schemaVersion = "1"
    caseId = $case.caseId
    framework = $case.framework
    workload = $case.workload
    achievedRequestsPerSecond = 4000.0
    outcomes = [ordered]@{
        started = $completed
        completed = $completed
        succeeded = $completed
        rejected = 0
        corrupt = 0
        misrouted = 0
        timedOut = 0
        disconnected = 0
        canceledAtDrain = 0
        duplicateResponses = 0
    }
    histogram = [ordered]@{
        unit = $case.histogram.unit
        lowestDiscernibleValue = $case.histogram.lowestDiscernibleValue
        highestTrackableValue = $case.histogram.highestTrackableValue
        significantDigits = $case.histogram.significantDigits
        totalCount = $completed
        maximum = 100
        buckets = @(
            [ordered]@{ upperBound = 100; count = $completed }
        )
    }
    metadata = [ordered]@{
        runtime = "PowerShell 7"
        transport = "fixture"
        serializer = "json"
    }
}

if ($behavior -eq "corrupt-result") {
    $result.caseId = "wrong-case"
}

$resultDirectory = Split-Path -Parent $ResultFile
New-Item -ItemType Directory -Force -Path $resultDirectory | Out-Null
$result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $ResultFile
