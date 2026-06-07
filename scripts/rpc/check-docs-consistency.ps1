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
        Pattern = "ValueTask.FromResult"
        Reason = "forbidden ValueTask pattern in Unity-compatible code examples"
        Allow = {
            param($relativePath, $line)
            $relativePath -eq "CONTRIBUTING.md" -and $line.Trim() -eq "- ``ValueTask.FromResult(...)``"
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

if ($failures.Count -gt 0) {
    Write-Error ("Documentation consistency check failed:`n" + ($failures -join "`n"))
    exit 1
}

if (-not $Quiet) {
    Write-Host "Documentation consistency check passed."
}
