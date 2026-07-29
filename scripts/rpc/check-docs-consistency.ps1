param(
    [switch]$Quiet
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "../..")
$markdownTargets = @(
    "README.md",
    "CONTRIBUTING.md",
    "blog",
    "docs"
)

$forbiddenSnippets = @(
    @{
        Pattern = "samples/RpcCall/RpcCall"
        Reason = "stale pre-variant sample path"
    },
    @{
        Pattern = "Game.Rpc.Runtime"
        Reason = "stale runtime assembly name"
    },
    @{
        Pattern = "``Lakona:StartupActors``"
        Reason = "removed configuration key"
        Allow = {
            param($relativePath, $line)
            $relativePath -eq "docs/configuration.md" -and
                $line.Contains("has been removed and")
        }
    },
    @{
        Pattern = "ValueTask.FromResult"
        Reason = "forbidden ValueTask pattern in Unity-compatible code examples"
        Allow = {
            param($relativePath, $line)
            ($relativePath -eq "CONTRIBUTING.md" -and $line.Trim() -eq "- ``ValueTask.FromResult(...)``") -or
            ($relativePath -eq "docs/contributing/engineering.md" -and $line.Contains("Do not use ``ValueTask.CompletedTask`` or ``ValueTask.FromResult(...)``"))
        }
    }
)

$files = foreach ($target in $markdownTargets) {
    $path = Join-Path $repoRoot $target
    if (Test-Path $path -PathType Leaf) {
        Get-Item $path
    } elseif (Test-Path $path -PathType Container) {
        Get-ChildItem $path -Recurse -File -Filter "*.md"
    }
}

$failures = New-Object System.Collections.Generic.List[string]

foreach ($file in $files) {
    $relativePath = [System.IO.Path]::GetRelativePath($repoRoot, $file.FullName).Replace("\", "/")
    $lines = Get-Content -LiteralPath $file.FullName

    for ($i = 0; $i -lt $lines.Count; $i++) {
        foreach ($snippet in $forbiddenSnippets) {
            if ($lines[$i].Contains($snippet.Pattern)) {
                $isAllowed = $false
                if ($snippet.ContainsKey("Allow")) {
                    $isAllowed = & $snippet.Allow $relativePath $lines[$i]
                }

                if (-not $isAllowed) {
                    $failures.Add(("{0}:{1}: {2} ({3})" -f $relativePath, ($i + 1), $snippet.Pattern, $snippet.Reason))
                }
            }
        }
    }
}

$changelogPath = Join-Path $repoRoot "CHANGELOG.md"
$changelogLines = Get-Content -LiteralPath $changelogPath
for ($i = 0; $i -lt $changelogLines.Count; $i++) {
    $line = $changelogLines[$i]
    if ($line.StartsWith("## ") -and $line -notmatch '^## \d{4}-\d{2}-\d{2} — \S') {
        $failures.Add(("CHANGELOG.md:{0}: milestone headings must use '## YYYY-MM-DD — Title'" -f ($i + 1)))
    }

    if ($line.StartsWith("**Key releases:**") -and $line -notmatch '`[^`]+ \d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?`') {
        $failures.Add(("CHANGELOG.md:{0}: Key releases must include a package ID and semantic version in backticks" -f ($i + 1)))
    }
}

if ($failures.Count -gt 0) {
    Write-Error ("Documentation consistency check failed:`n" + ($failures -join "`n"))
    exit 1
}

if (-not $Quiet) {
    Write-Host "Documentation consistency check passed."
}
