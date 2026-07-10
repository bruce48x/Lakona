#Requires -Version 7.0

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$ResultsPath,

    [Parameter(Mandatory)]
    [string]$TargetTestName
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $ResultsPath -PathType Leaf)) {
    throw "Test result XML was not created: $ResultsPath"
}

try {
    [xml]$resultDocument = Get-Content -LiteralPath $ResultsPath -Raw
}
catch {
    throw "Test result XML could not be parsed: $ResultsPath. $($_.Exception.Message)"
}

$testRun = $resultDocument.SelectSingleNode("/test-run")
if ($null -eq $testRun) {
    throw "Test result XML does not contain a test-run root: $ResultsPath"
}

$runResult = $testRun.GetAttribute("result")
if (-not [string]::Equals($runResult, "Passed", [StringComparison]::OrdinalIgnoreCase)) {
    throw "Unity test run result was '$runResult', not Passed: $ResultsPath"
}

$counts = @{}
foreach ($countName in @("total", "passed", "failed", "skipped", "inconclusive")) {
    $count = 0
    $countText = $testRun.GetAttribute($countName)
    if (-not [int]::TryParse($countText, [ref]$count)) {
        throw "Unity test run has an invalid '$countName' count '$countText': $ResultsPath"
    }

    $counts[$countName] = $count
}

foreach ($countName in @("failed", "skipped", "inconclusive")) {
    if ($counts[$countName] -ne 0) {
        throw "Unity test run reported $($counts[$countName]) $countName test(s): $ResultsPath"
    }
}

if ($counts["total"] -ne 1 -or $counts["passed"] -ne 1) {
    throw "Unity test run must contain exactly one test and one pass, but reported total=$($counts["total"]) and passed=$($counts["passed"]): $ResultsPath"
}

$targetCases = @($resultDocument.SelectNodes("//test-case") | Where-Object {
    [string]::Equals($_.GetAttribute("fullname"), $TargetTestName, [StringComparison]::Ordinal)
})
if ($targetCases.Count -ne 1) {
    throw "Expected exactly one result for '$TargetTestName', but found $($targetCases.Count): $ResultsPath"
}

$targetResult = $targetCases[0].GetAttribute("result")
if (-not [string]::Equals($targetResult, "Passed", [StringComparison]::OrdinalIgnoreCase)) {
    throw "Unity target test '$TargetTestName' result was '$targetResult', not Passed: $ResultsPath"
}
