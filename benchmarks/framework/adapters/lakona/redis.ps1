param(
    [Parameter(Mandatory)][string]$Name,
    [int]$Port,
    [string]$Image,
    [switch]$Stop
)
$ErrorActionPreference = 'Stop'
if ($Name -notmatch '^lakona-framework-benchmark-[a-zA-Z0-9_.-]+$') {
    throw 'Redis container name must belong to this benchmark run.'
}
if ($Stop) {
    $existing = & docker ps --all --quiet --filter "name=^/$Name$"
    if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect benchmark Redis resources.' }
    if ($existing) {
        & docker rm --force --volumes $Name
        if ($LASTEXITCODE -ne 0) { throw 'Cannot remove benchmark Redis container.' }
    }
    exit 0
}
$container = & docker run --detach --name $Name --label dev.lakona.framework-benchmark=true `
    --publish "127.0.0.1:${Port}:6379" --tmpfs /data $Image redis-server --save '' --appendonly no
if ($LASTEXITCODE -ne 0) { throw 'Cannot start benchmark Redis container.' }
$deadline = [DateTimeOffset]::UtcNow.AddSeconds(30)
do {
    $ping = & docker exec $Name redis-cli ping 2>$null
    if ($LASTEXITCODE -eq 0 -and $ping -eq 'PONG') { break }
    if ([DateTimeOffset]::UtcNow -ge $deadline) { throw 'Benchmark Redis did not become ready.' }
    Start-Sleep -Milliseconds 200
} while ($true)
@{ event = 'ready'; role = 'membership'; nodeId = 'redis'; endpoints = @{} } | ConvertTo-Json -Compress
while ($true) {
    Start-Sleep -Seconds 1
    $running = & docker inspect --format '{{.State.Running}}' $Name 2>$null
    if ($LASTEXITCODE -ne 0 -or $running -ne 'true') { throw 'Benchmark Redis stopped unexpectedly.' }
}
