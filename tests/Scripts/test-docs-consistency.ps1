#Requires -Version 7.0
$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
. (Join-Path $repoRoot 'scripts/rpc/docs-consistency-rules.ps1')
$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) ('lakona-docs-' + [guid]::NewGuid().ToString('N'))
function Write-Fixture {
    param([string]$RelativePath, [string]$Content)
    $path = Join-Path $fixtureRoot $RelativePath
    New-Item -ItemType Directory -Force -Path (Split-Path $path) | Out-Null
    Set-Content -LiteralPath $path -Value $Content
    return $path
}
function Assert-Result {
    param($Failures, [string]$Expected = '')
    $messages = @($Failures) -join "`n"
    if ((!$Expected -and $messages) -or ($Expected -and $messages -notlike "*$Expected*")) {
        throw "Expected '$Expected'; got '$messages'"
    }
}
try {
    $source = Write-Fixture 'docs/source.md' '# Source'
    $null = Write-Fixture 'docs/target file.md' @'
# Target
## Repeat
## Repeat
<a id="explicit-anchor"></a>
```text
## Not a heading
```
'@
    Assert-Result (Test-DocumentationLinks $source $fixtureRoot '[ok](<target file.md#repeat-1>) [id](<target file.md#explicit-anchor>) [external](https://example.invalid/missing)')
    Assert-Result (Test-DocumentationLinks $source $fixtureRoot '[missing](missing.md)') 'missing local link target'
    Assert-Result (Test-DocumentationLinks $source $fixtureRoot '[missing](<target file.md#not-a-heading>)') 'missing local anchor'
    Assert-Result (Test-DocumentationLinks $source $fixtureRoot '[ref]: <target file.md#missing>') 'missing local anchor'
    $parts = Get-DocumentationParts "~~~text`n[example](missing.md)`n~~~"
    Assert-Result (Test-DocumentationLinks $source $fixtureRoot $parts.Text)
    $article = Write-Fixture 'blog/content/posts/first.md' '# First'
    $null = Write-Fixture 'blog/content/posts/second.md' '# Second'
    Assert-Result (Test-DocumentationLinks $article $fixtureRoot '[article](../second/#second)')

    $null = Write-Fixture 'scripts/rpc/sample.ps1' @'
param([ValidateSet("Rpc.Unity.Test")][string]$Sample)
throw 'Documentation commands must never execute.'
'@
    Assert-Result (Test-DocumentationExamples $source $fixtureRoot @('.\scripts\rpc\sample.ps1 -Sample Rpc.Unity.Test'))
    Assert-Result (Test-DocumentationExamples $source $fixtureRoot @('.\scripts\sample.ps1 -Sample Rpc.Unity.Test')) 'missing script in example'
    Assert-Result (Test-DocumentationExamples $source $fixtureRoot @('.\scripts\rpc\sample.ps1 -Sample Unity.Test')) 'unsupported -Sample'
    $null = Write-Fixture '.agents/skills/example/scripts/check.ps1' 'throw "Never execute"'
    Assert-Result (Test-DocumentationExamples $source $fixtureRoot @('.\.agents\skills\example\scripts\check.ps1'))
    $longBlock = (1..8 | ForEach-Object { "var item$_ = $_;" }) -join "`n"
    Assert-Result (Test-DocumentationExamples $source $fixtureRoot @($longBlock, $longBlock)) 'repeated identical code block'
    Assert-Result (Test-DocumentationExamples $source $fixtureRoot @('return default;', 'return default;'))

    $validChangelog = @'
## 2026-11-11 — Example

**Key releases:** `Lakona.Hub 1.2.3-preview.1+build.4`.

- Compatibility milestone.
'@
    Assert-Result (Test-DocumentationChangelog $validChangelog)
    Assert-Result (Test-DocumentationChangelog $validChangelog.Replace('Lakona.Hub', 'Lakona Hub')) 'invalid package ID/version'
    Assert-Result (Test-DocumentationChangelog $validChangelog.Replace('Key releases:', 'Key release:')) "must use 'Key releases'"
    Assert-Result (Test-DocumentationChangelog ($validChangelog + "`n- Two`n- Three`n- Four")) 'expected one to three'
    Assert-Result (Test-DocumentationChangelog ($validChangelog + "`n" + $validChangelog)) 'duplicate milestone date'
    Assert-Result (Test-DocumentationChangelog $validChangelog.Replace('2026-11-11', '2026-02-30')) 'invalid date'
    Assert-Result (Test-DocumentationChangelog ($validChangelog.Replace('2026-11-11', '2026-11-10') + "`n" + $validChangelog)) 'newest first'
    Assert-Result (Test-DocumentationChangelog $validChangelog.Replace('1.2.3-preview.1+build.4', '1.2')) 'invalid package ID/version'
    Assert-Result (Test-DocumentationChangelog $validChangelog.Replace('`Lakona.Hub 1.2.3-preview.1+build.4`', '`Lakona.Game.Clustering.*` providers at `0.1.0`')) 'explicit package IDs'

    # The public entry point must include samples, both skill trees and untracked
    # maintained files, while ignoring embedded third-party packages.
    & git init -q $fixtureRoot
    if ($LASTEXITCODE -ne 0) { throw 'Could not initialize documentation fixture.' }
    $null = Write-Fixture 'README.md' '[Cluster](docs/cluster.md)'
    $null = Write-Fixture 'docs/cluster.md' '# Cluster'
    $null = Write-Fixture 'src/Lakona.Tool/README.md' '[Actors and Hotfix](https://github.com/bruce48x/Lakona/blob/main/docs/actor.md)'
    $null = Write-Fixture 'src/Lakona.Rpc.Analyzers/README.md' 'Consumers must not reference `Lakona.Rpc.Analyzers` directly.'
    $null = Write-Fixture 'CHANGELOG.md' $validChangelog
    $null = Write-Fixture 'samples/Client/Assets/Packages/ThirdParty/README.md' '[upstream](absent.md)'
    $entryPoint = Join-Path $repoRoot 'scripts/rpc/check-docs-consistency.ps1'
    & pwsh -NoProfile -File $entryPoint -RepositoryRoot $fixtureRoot -Quiet
    if ($LASTEXITCODE -ne 0) { throw 'Valid documentation fixture failed.' }
    foreach ($scope in @('samples/Example', 'skills/example', '.agents/skills/example')) {
        $badDoc = Write-Fixture "$scope/README.md" '[broken](missing.md)'
        $output = & pwsh -NoProfile -File $entryPoint -RepositoryRoot $fixtureRoot -Quiet 2>&1
        if ($LASTEXITCODE -eq 0 -or ($output -join "`n") -notlike '*missing local link target*') {
            throw "Broken untracked documentation was not rejected in $scope"
        }
        Remove-Item -LiteralPath $badDoc
    }
}
finally {
    $resolvedFixture = [IO.Path]::GetFullPath($fixtureRoot)
    $tempBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    if (!$resolvedFixture.StartsWith($tempBase, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($resolvedFixture) -notlike 'lakona-docs-*') { throw 'Unexpected fixture cleanup path.' }
    Remove-Item -LiteralPath $resolvedFixture -Recurse -Force
}
Write-Host 'Documentation consistency contract: PASS'
