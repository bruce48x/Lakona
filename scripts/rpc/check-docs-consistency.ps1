param(
    [switch]$Quiet,
    [string]$RepositoryRoot = (Join-Path $PSScriptRoot '../..')
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path $RepositoryRoot).Path
. (Join-Path $PSScriptRoot 'docs-consistency-rules.ps1')

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
                ($relativePath -eq "docs/contributing/engineering.md" -and $line.Contains("Do not use ``ValueTask.CompletedTask`` or ``ValueTask.FromResult(...)``")) -or
                ($relativePath -eq 'skills/lakona-implement-service/SKILL.md' -and $line.Contains('`ValueTask.CompletedTask` or `ValueTask.FromResult(...)` in Lakona projects')) -or
                ($relativePath -eq '.agents/skills/lakona-architecture-review/references/standards-checklist.md' -and $line.Contains('with no `ValueTask.CompletedTask` or `ValueTask.FromResult(...)`'))
        }
    },
    @{
        Pattern = "npx skills add"
        Reason = "generated projects already contain the transactional Skill Pack"
    },
    @{
        Pattern = "public static partial class RoomBehavior"
        Reason = "Actor behavior APIs are instance methods on sealed partial behavior classes"
    },
    @{
        Pattern = "AddLakonaGameHotfix"
        Reason = "generated applications compose Hotfix through the LakonaGameServer hosting facade"
    },
    @{
        Pattern = "PublishReliablePushAsync"
        Reason = "business notifications publish through generated IClientNotifications targets"
    },
    @{
        Pattern = "SQL-backed node"
        Reason = "Membership uses a shared Membership Table; Actor Directory state remains in memory"
    },
    @{
        Pattern = "process-local route directory"
        Reason = "distributed routing uses the Membership Table and in-memory Actor Directory"
    },
    @{
        Pattern = "reference this package as a private build dependency"
        Reason = "Lakona.Rpc.Analyzers is embedded in its runtime owner package"
    }
)

$requiredSnippets = @(
    @{
        Path = "README.md"
        Pattern = "[Cluster](docs/cluster.md)"
        Reason = "top-level cluster guidance must delegate detailed semantics to the authority"
    },
    @{
        Path = "src/Lakona.Tool/README.md"
        Pattern = "[Actors and Hotfix](https://github.com/bruce48x/Lakona/blob/main/docs/actor.md)"
        Reason = "Tool package guidance must link to the maintained Actor authoring contract"
    },
    @{
        Path = "src/Lakona.Rpc.Analyzers/README.md"
        Pattern = "Consumers must not reference ``Lakona.Rpc.Analyzers`` directly."
        Reason = "Analyzer implementation ownership must remain explicit"
    }
)

$paths = & git -c core.quotePath=false -C $repoRoot ls-files --cached --others --exclude-standard -- '*.md'
if ($LASTEXITCODE -ne 0) { throw 'Could not inventory maintained documentation.' }
$files = @($paths | Sort-Object -Unique | Where-Object {
    $_ -notmatch '(^|/)(Packages|Library|Temp|bin|obj|node_modules|\.godot|\.tmp|artifacts|\.nuget)(/|$)' -and
    (Test-Path -LiteralPath (Join-Path $repoRoot $_) -PathType Leaf)
} | ForEach-Object { Get-Item -LiteralPath (Join-Path $repoRoot $_) })

$failures = New-Object System.Collections.Generic.List[string]

foreach ($file in $files) {
    $relativePath = [System.IO.Path]::GetRelativePath($repoRoot, $file.FullName).Replace("\", "/")
    $lines = @(Get-Content -LiteralPath $file.FullName)
    $parts = Get-DocumentationParts ($lines -join "`n")
    foreach ($failure in (Test-DocumentationLinks $file.FullName $repoRoot $parts.Text)) { $failures.Add($failure) }
    foreach ($failure in (Test-DocumentationExamples $file.FullName $repoRoot $parts.Blocks)) { $failures.Add($failure) }

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

foreach ($required in $requiredSnippets) {
    $path = Join-Path $repoRoot $required.Path
    $content = Get-Content -LiteralPath $path -Raw
    if (-not $content.Contains($required.Pattern)) {
        $failures.Add(("{0}: missing '{1}' ({2})" -f $required.Path, $required.Pattern, $required.Reason))
    }
}

$changelogPath = Join-Path $repoRoot "CHANGELOG.md"
foreach ($failure in (Test-DocumentationChangelog (Get-Content -LiteralPath $changelogPath -Raw))) { $failures.Add($failure) }

if ($failures.Count -gt 0) {
    Write-Error ("Documentation consistency check failed:`n" + ($failures -join "`n"))
    exit 1
}

if (-not $Quiet) {
    Write-Host "Documentation consistency check passed ($($files.Count) maintained Markdown files)."
}
