$ErrorActionPreference = 'Stop'
$image = 'redis@sha256:6ab0b6e7381779332f97b8ca76193e45b0756f38d4c0dcda72dbb3c32061ab99'
& docker info --format '{{.ServerVersion}}'
if ($LASTEXITCODE -ne 0) { throw 'The native Lakona cluster benchmark requires a running Docker engine for Redis.' }
& docker image inspect $image --format '{{.Id}}' 2>$null
if ($LASTEXITCODE -ne 0) {
    & docker pull $image
    if ($LASTEXITCODE -ne 0) { throw 'Cannot prepare the pinned Redis image.' }
}
$revision = (& git -C $PSScriptRoot rev-parse HEAD).Trim()
$changes = & git -C $PSScriptRoot status --porcelain --untracked-files=normal
if ($changes) { $revision += '-dirty' }
foreach ($project in @('Hotfix', 'Driver')) {
    $projectPath = Join-Path $PSScriptRoot "FrameworkBenchmark.Lakona.$project/FrameworkBenchmark.Lakona.$project.csproj"
    & dotnet build $projectPath --configuration Release "-p:BenchmarkRevision=$revision"
    if ($LASTEXITCODE -ne 0) { throw "Cannot build Lakona benchmark $project." }
}
