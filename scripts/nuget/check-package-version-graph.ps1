param(
    [string] $Base,
    [string] $Head = "HEAD"
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
Push-Location $repoRoot
try {
    if (-not [string]::IsNullOrWhiteSpace($Base)) {
        $env:LAKONA_VERSION_GUARD_BASE = $Base
        $env:LAKONA_VERSION_GUARD_HEAD = $Head
    }

    dotnet test "tests/Lakona.RepositoryGuards.Tests/Lakona.RepositoryGuards.Tests.csproj" --filter "PackageVersionGraph"
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}
finally {
    Pop-Location
}
