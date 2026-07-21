$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
Push-Location $repoRoot
try {
    dotnet test "tests/Lakona.RepositoryGuards.Tests/Lakona.RepositoryGuards.Tests.csproj" --no-restore --filter "PackageVersionGraph|HubVersion|ProjectSystemConsumerVersion"
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}
finally {
    Pop-Location
}
