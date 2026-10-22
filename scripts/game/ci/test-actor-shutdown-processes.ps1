#Requires -Version 7.0
<#
.SYNOPSIS
Runs deterministic Actor handoffs during graceful shutdown over real TCP and PostgreSQL.
.DESCRIPTION
Requires .NET 10, Docker and Python 3.10+. Uses an isolated temporary PostgreSQL
container and hidden child consoles on Windows. No force-kill is used to pass
shutdown assertions. Evidence is retained beneath artifacts/shutdown-processes.
#>
[CmdletBinding()]
param([string]$Python = "python", [switch]$SkipBuild)

$ErrorActionPreference = "Stop"
$repo = (Resolve-Path (Join-Path $PSScriptRoot "../../..")).Path
$artifacts = Join-Path $repo "artifacts/shutdown-processes"
New-Item -ItemType Directory -Force -Path $artifacts | Out-Null
if (-not $SkipBuild) {
    & dotnet build (Join-Path $repo "tests/Lakona.Game.ProcessTests.Hotfix/Lakona.Game.ProcessTests.Hotfix.csproj") --artifacts-path (Join-Path $artifacts "build") -c Release --nologo -v minimal *> (Join-Path $artifacts "build.log")
    if ($LASTEXITCODE -ne 0) {
        Get-Content (Join-Path $artifacts "build.log") -Tail 50
        throw "Process-test fixture build failed."
    }
}
& $Python (Join-Path $PSScriptRoot "actor_shutdown_processes.py") --repository $repo
if ($LASTEXITCODE -ne 0) { throw "Cross-process Actor shutdown verification failed." }
