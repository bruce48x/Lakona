param(
    [Parameter(Mandatory = $true)]
    [string]$ClientPath
)

$ErrorActionPreference = "Stop"
$forbiddenSegments = @("net10.0", "net9.0", "net8.0", "net7.0", "net6.0", "net472", "net48", "net481")
$fallbackSegment = "netstandard2.0"
$preferredSegment = "netstandard2.1"
$packagesRoot = Join-Path $ClientPath "Assets/Packages"
if (-not (Test-Path $packagesRoot)) {
    Write-Host "No Assets/Packages at $packagesRoot; skipping."
    exit 0
}

function Test-ForbiddenTfmPath {
    param([Parameter(Mandatory = $true)][string]$NormalizedPath)

    foreach ($segment in $forbiddenSegments) {
        $pattern = "/lib/{0}/" -f [regex]::Escape($segment)
        if ($NormalizedPath -match $pattern) {
            return $true
        }
    }

    return $false
}

function Test-ShadowedFallbackTfmPath {
    param([Parameter(Mandatory = $true)][string]$NormalizedPath)

    $fallbackPattern = "/lib/{0}/" -f [regex]::Escape($fallbackSegment)
    if ($NormalizedPath -notmatch $fallbackPattern) {
        return $false
    }

    $preferredPath = $NormalizedPath -replace $fallbackPattern, ("/lib/{0}/" -f $preferredSegment)
    return Test-Path -LiteralPath $preferredPath
}

function Test-EnabledPluginCompatibility {
    param([Parameter(Mandatory = $true)][string]$Text)

    $platformNames = "Any|Editor|Standalone|Win|Win64|OSXUniversal|Linux64"
    $platformBlockPattern = "(?ms)-\s*first:\s*\r?\n\s*(?::\s*)?($platformNames):?\s*\r?\n\s*second:\s*\r?\n\s*enabled:\s*1"
    $legacyEditorPattern = "editorCompatibility:\s*1"
    $buildTargetPattern = "(?ms)buildTarget:\s*($platformNames)\b.*?enabled:\s*1"

    return $Text -match $platformBlockPattern -or
        $Text -match $legacyEditorPattern -or
        $Text -match $buildTargetPattern
}

$violations = @()
Get-ChildItem -Path $packagesRoot -Recurse -Filter "*.dll.meta" | ForEach-Object {
    $normalized = $_.FullName.Replace('\', '/')
    $reason = $null
    if (Test-ForbiddenTfmPath $normalized) {
        $reason = "forbidden TFM enabled"
    }
    elseif (Test-ShadowedFallbackTfmPath $normalized) {
        $reason = "netstandard2.0 fallback enabled while netstandard2.1 sibling exists"
    }
    else {
        return
    }

    $text = Get-Content -Raw -LiteralPath $_.FullName
    if (Test-EnabledPluginCompatibility $text) {
        $violations += "$normalized ($reason)"
    }
}

if ($violations.Count -gt 0) {
    Write-Error ("Unity NuGet plugin policy violations:`n" + ($violations -join "`n"))
}

Write-Host "Unity NuGet plugin policy check passed for $ClientPath"
