#Requires -Version 5.1
<#
.SYNOPSIS
    Validates Lakona.Tool generated projects against locally packed Lakona NuGet packages.

.DESCRIPTION
    Packs current src/Lakona.* projects into a local feed, scaffolds generated
    projects with Lakona.Tool, builds the generated server, and optionally runs
    a runtime RPC verification client.
#>

[CmdletBinding()]
param(
    [ValidateSet("all", "unity", "unity-cn", "tuanjie", "godot")]
    [string]$Engine = "godot",

    [ValidateSet("all", "tcp", "kcp", "websocket")]
    [string]$Transport = "websocket",

    [ValidateSet("all", "json", "memorypack")]
    [string]$Serializer = "memorypack",

    [switch]$Runtime,

    [switch]$KeepScaffolds,

    [int]$Port = 20000,

    [string]$WorkDir = ".tmp/lakona-local-package-e2e"
)

$ErrorActionPreference = "Stop"

function Write-Banner {
    param([string]$Text)

    Write-Host ""
    Write-Host ("=" * 72) -ForegroundColor Cyan
    Write-Host "  $Text" -ForegroundColor Cyan
    Write-Host ("=" * 72) -ForegroundColor Cyan
}

function Resolve-RepoRoot {
    $current = Resolve-Path "."
    while ($current) {
        if (Test-Path (Join-Path $current "CONTRIBUTING.md")) {
            return $current.Path
        }

        $parent = Split-Path -Parent $current
        if ([string]::IsNullOrWhiteSpace($parent) -or $parent -eq $current) {
            break
        }

        $current = Resolve-Path $parent
    }

    throw "Could not find repository root containing CONTRIBUTING.md."
}

function Get-TransportPackageName {
    param([string]$Value)

    switch ($Value.ToLowerInvariant()) {
        "tcp" { "Lakona.Rpc.Transport.Tcp" }
        "kcp" { "Lakona.Rpc.Transport.Kcp" }
        "websocket" { "Lakona.Rpc.Transport.WebSocket" }
        default { throw "Unsupported transport: $Value" }
    }
}

function Get-SerializerPackageName {
    param([string]$Value)

    switch ($Value.ToLowerInvariant()) {
        "json" { "Lakona.Rpc.Serializer.Json" }
        "memorypack" { "Lakona.Rpc.Serializer.MemoryPack" }
        default { throw "Unsupported serializer: $Value" }
    }
}

function Get-TransportUsing {
    param([string]$Value)

    switch ($Value.ToLowerInvariant()) {
        "tcp" { "using Lakona.Rpc.Transport.Tcp;" }
        "kcp" { "using Lakona.Rpc.Transport.Kcp;" }
        "websocket" { "using Lakona.Rpc.Transport.WebSocket;" }
        default { throw "Unsupported transport: $Value" }
    }
}

function Get-SerializerUsing {
    param([string]$Value)

    switch ($Value.ToLowerInvariant()) {
        "json" { "using Lakona.Rpc.Serializer.Json;" }
        "memorypack" { "using Lakona.Rpc.Serializer.MemoryPack;" }
        default { throw "Unsupported serializer: $Value" }
    }
}

function Get-TransportConstructor {
    param(
        [string]$Value,
        [int]$Port
    )

    switch ($Value.ToLowerInvariant()) {
        "tcp" { "new TcpTransport(""127.0.0.1"", $Port)" }
        "kcp" { "new KcpTransport(""127.0.0.1"", $Port)" }
        "websocket" { "new WsTransport(""ws://127.0.0.1:$Port/ws"")" }
        default { throw "Unsupported transport: $Value" }
    }
}

function Get-SerializerConstructor {
    param([string]$Value)

    switch ($Value.ToLowerInvariant()) {
        "json" { "new JsonRpcSerializer()" }
        "memorypack" { "new MemoryPackRpcSerializer()" }
        default { throw "Unsupported serializer: $Value" }
    }
}

function Get-LocalPackageVersion {
    param(
        [string]$FeedDir,
        [string]$PackageId
    )

    $prefix = "$PackageId."
    $package = Get-ChildItem -LiteralPath $FeedDir -Filter "$PackageId.*.nupkg" |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1

    if (-not $package) {
        throw "Local feed is missing $PackageId."
    }

    $name = [System.IO.Path]::GetFileNameWithoutExtension($package.Name)
    if (-not $name.StartsWith($prefix, [StringComparison]::Ordinal)) {
        throw "Could not parse package version from $($package.Name)."
    }

    return $name.Substring($prefix.Length)
}

function Write-NuGetConfig {
    param(
        [string]$Path,
        [string]$FeedDir
    )

    $escapedFeed = [System.Security.SecurityElement]::Escape($FeedDir)
    $content = @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local-lakona" value="$escapedFeed" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
"@

    Set-Content -LiteralPath $Path -Value $content -Encoding UTF8
}

function Set-GeneratedServerPort {
    param(
        [string]$ProjectDir,
        [int]$Port
    )

    if ($Port -eq 20000) {
        return
    }

    $appSettings = Join-Path $ProjectDir "Server/App/appsettings.json"
    if (-not (Test-Path $appSettings)) {
        return
    }

    $content = Get-Content -LiteralPath $appSettings -Raw
    $content = $content -replace '("Port"\s*:\s*)\d+', "`${1}$Port"
    Set-Content -LiteralPath $appSettings -Value $content -Encoding UTF8
}

function Test-PortFree {
    param([int]$Port)

    try {
        $connections = Get-NetTCPConnection -LocalPort $Port -ErrorAction SilentlyContinue
        return -not $connections
    } catch {
        return $true
    }
}

function New-E2EClient {
    param(
        [string]$ProjectDir,
        [string]$FeedDir,
        [string]$Transport,
        [string]$Serializer,
        [int]$Port
    )

    $e2eDir = Join-Path $ProjectDir "E2EVerification"
    New-Item -ItemType Directory -Force -Path $e2eDir | Out-Null

    $rpcCoreVersion = Get-LocalPackageVersion $FeedDir "Lakona.Rpc.Core"
    $rpcClientVersion = Get-LocalPackageVersion $FeedDir "Lakona.Rpc.Client"
    $transportPackage = Get-TransportPackageName $Transport
    $transportVersion = Get-LocalPackageVersion $FeedDir $transportPackage
    $serializerPackage = Get-SerializerPackageName $Serializer
    $serializerVersion = Get-LocalPackageVersion $FeedDir $serializerPackage
    $sharedProj = (Resolve-Path (Join-Path $ProjectDir "Shared/Shared.csproj")).Path

    $csproj = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Lakona.Rpc.Core" Version="$rpcCoreVersion" />
    <PackageReference Include="Lakona.Rpc.Client" Version="$rpcClientVersion" />
    <PackageReference Include="$transportPackage" Version="$transportVersion" />
    <PackageReference Include="$serializerPackage" Version="$serializerVersion" />
    <ProjectReference Include="$sharedProj" />
  </ItemGroup>
</Project>
"@

    Set-Content -LiteralPath (Join-Path $e2eDir "E2EVerification.csproj") -Value $csproj -Encoding UTF8

    $transportUsing = Get-TransportUsing $Transport
    $serializerUsing = Get-SerializerUsing $Serializer
    $transportCtor = Get-TransportConstructor $Transport $Port
    $serializerCtor = Get-SerializerConstructor $Serializer

    $program = @"
using Shared.Contracts.Chat;
using Lakona.Rpc.Client;
using Lakona.Rpc.Core;
$transportUsing
$serializerUsing

try
{
    var transport = $transportCtor;
    var serializer = $serializerCtor;
    await using var client = new RpcClientRuntime(transport, serializer);

    Console.WriteLine("[E2E] Connecting to server...");
    await client.StartAsync();
    Console.WriteLine("[E2E] Connected.");

    var reply = await client.CallAsync(
        new RpcMethod<LoginRequest, LoginReply>(1, 1),
        new LoginRequest { PlayerName = "E2ETest" });

    Console.WriteLine("[E2E] Members={0}, RecentMessages={1}", reply.Members.Count, reply.RecentMessages.Count);
    if (reply.Members.Count == 1 && reply.Members[0].Name == "E2ETest")
    {
        Console.WriteLine("[E2E] SUCCESS");
        return;
    }

    Console.Error.WriteLine("[E2E] FAIL: Unexpected login response.");
    Environment.Exit(1);
}
catch (Exception ex)
{
    Console.Error.WriteLine("[E2E] FAIL: {0}", ex);
    Environment.Exit(1);
}
"@

    Set-Content -LiteralPath (Join-Path $e2eDir "Program.cs") -Value $program -Encoding UTF8
    Write-NuGetConfig (Join-Path $e2eDir "NuGet.config") $FeedDir
    return $e2eDir
}

$repoRoot = Resolve-RepoRoot
Set-Location $repoRoot

$workRoot = Join-Path $repoRoot $WorkDir
$feedDir = Join-Path $workRoot "feed"
$packageCache = Join-Path $workRoot "packages"
$scaffoldRoot = Join-Path $workRoot "scaffolds"
$reportPath = Join-Path $workRoot "report.md"
$summaryPath = Join-Path $workRoot "summary.json"

New-Item -ItemType Directory -Force -Path $feedDir, $packageCache, $scaffoldRoot | Out-Null

$env:DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER = "1"
$env:NUGET_PACKAGES = $packageCache

$engines = if ($Engine -eq "all") { @("unity", "unity-cn", "tuanjie", "godot") } else { @($Engine) }
$transports = if ($Transport -eq "all") { @("tcp", "kcp", "websocket") } else { @($Transport) }
$serializers = if ($Serializer -eq "all") { @("json", "memorypack") } else { @($Serializer) }

Write-Banner "Packing local Lakona packages"
$packageProjects = Get-ChildItem -Path (Join-Path $repoRoot "src") -Recurse -Filter "Lakona.*.csproj" |
    Sort-Object FullName

foreach ($project in $packageProjects) {
    $name = [System.IO.Path]::GetFileNameWithoutExtension($project.FullName)
    Write-Host "Packing $name..." -ForegroundColor DarkGray
    dotnet pack $project.FullName -c Release -o $feedDir --nologo -v q
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet pack failed for $($project.FullName)."
    }
}

Write-Banner "Building Lakona.Tool"
dotnet build (Join-Path $repoRoot "src/Lakona.Tool/Lakona.Tool.csproj") --nologo -v q
if ($LASTEXITCODE -ne 0) {
    throw "Lakona.Tool build failed."
}

$results = New-Object System.Collections.Generic.List[object]
$total = $engines.Count * $transports.Count * $serializers.Count
$index = 0

foreach ($engineValue in $engines) {
    foreach ($transportValue in $transports) {
        foreach ($serializerValue in $serializers) {
            $index++
            $projectName = "LocalPkg_$($engineValue)_$($transportValue)_$($serializerValue)" -replace "[^A-Za-z0-9_]", "_"
            $projectDir = Join-Path $scaffoldRoot $projectName
            $label = "[$index/$total] $engineValue / $transportValue / $serializerValue"

            Write-Banner $label

            $result = [ordered]@{
                Engine = $engineValue
                Transport = $transportValue
                Serializer = $serializerValue
                Scaffold = "FAIL"
                Build = "FAIL"
                Runtime = if ($Runtime) { "FAIL" } else { "SKIP" }
                ProjectDir = $projectDir
                Error = ""
            }

            $serverProc = $null

            try {
                if (Test-Path $projectDir) {
                    Remove-Item -LiteralPath $projectDir -Recurse -Force
                }

                dotnet run --project (Join-Path $repoRoot "src/Lakona.Tool") --no-build -- `
                    new `
                    --name $projectName `
                    --client-engine $engineValue `
                    --transport $transportValue `
                    --serializer $serializerValue `
                    --persistence none `
                    --nugetforunity-source embedded `
                    --deploy-profile none `
                    --output $scaffoldRoot

                if ($LASTEXITCODE -ne 0) {
                    $result.Error = "Scaffold failed."
                    $results.Add([pscustomobject]$result)
                    continue
                }

                $result.Scaffold = "PASS"
                Write-NuGetConfig (Join-Path $projectDir "NuGet.config") $feedDir
                Set-GeneratedServerPort $projectDir $Port

                $serverSln = Join-Path $projectDir "Server/Server.slnx"
                dotnet build $serverSln --nologo -v q
                if ($LASTEXITCODE -ne 0) {
                    $result.Error = "Generated server build failed."
                    $results.Add([pscustomobject]$result)
                    continue
                }

                $result.Build = "PASS"

                if ($Runtime) {
                    if (-not (Test-PortFree $Port)) {
                        throw "Port $Port is already in use. Re-run with -Port <free-port>."
                    }

                    $e2eDir = New-E2EClient $projectDir $feedDir $transportValue $serializerValue $Port
                    dotnet build (Join-Path $e2eDir "E2EVerification.csproj") --nologo -v q
                    if ($LASTEXITCODE -ne 0) {
                        $result.Error = "E2E client build failed."
                        $results.Add([pscustomobject]$result)
                        continue
                    }

                    $serverOut = Join-Path $projectDir "server-out.txt"
                    $serverErr = Join-Path $projectDir "server-err.txt"
                    $serverProject = Join-Path $projectDir "Server/App/Server.App.csproj"

                    $serverProc = Start-Process -FilePath "dotnet" `
                        -ArgumentList "run", "--project", $serverProject, "--no-build" `
                        -NoNewWindow `
                        -PassThru `
                        -RedirectStandardOutput $serverOut `
                        -RedirectStandardError $serverErr

                    $ready = $false
                    for ($i = 0; $i -lt 30; $i++) {
                        Start-Sleep -Seconds 1
                        if ($serverProc.HasExited) {
                            break
                        }

                        if (Test-Path $serverOut) {
                            $serverText = Get-Content -LiteralPath $serverOut -Raw -ErrorAction SilentlyContinue
                            if ($serverText -match "Application started|Now listening|listening|Listening") {
                                $ready = $true
                                break
                            }
                        }
                    }

                    if (-not $ready) {
                        $result.Error = "Server did not become ready. See $serverOut and $serverErr."
                        $results.Add([pscustomobject]$result)
                        continue
                    }

                    dotnet run --project (Join-Path $e2eDir "E2EVerification.csproj") --no-build
                    if ($LASTEXITCODE -ne 0) {
                        $result.Error = "Runtime E2E client failed."
                        $results.Add([pscustomobject]$result)
                        continue
                    }

                    $result.Runtime = "PASS"
                }

                $results.Add([pscustomobject]$result)
            } catch {
                $result.Error = $_.Exception.Message
                $results.Add([pscustomobject]$result)
            } finally {
                if ($serverProc -and -not $serverProc.HasExited) {
                    Stop-Process -Id $serverProc.Id -Force -ErrorAction SilentlyContinue
                }
            }
        }
    }
}

$passCount = ($results | Where-Object {
    $_.Scaffold -eq "PASS" -and $_.Build -eq "PASS" -and ($_.Runtime -eq "PASS" -or $_.Runtime -eq "SKIP")
}).Count
$failCount = $results.Count - $passCount

$results | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $summaryPath -Encoding UTF8

$report = New-Object System.Collections.Generic.List[string]
$report.Add("# Lakona Local Package E2E Report")
$report.Add("")
$report.Add("- Generated at: $([DateTimeOffset]::UtcNow.ToString("u"))")
$report.Add("- Runtime verification: $([bool]$Runtime)")
$report.Add("- Local feed: $feedDir")
$report.Add("- Isolated package cache: $packageCache")
$report.Add("- Passed: $passCount")
$report.Add("- Failed: $failCount")
$report.Add("")
$report.Add("| Engine | Transport | Serializer | Scaffold | Build | Runtime | Error |")
$report.Add("| --- | --- | --- | --- | --- | --- | --- |")
foreach ($item in $results) {
    $errorText = if ($item.Error) { $item.Error.Replace("|", "\|") } else { "" }
    $report.Add("| $($item.Engine) | $($item.Transport) | $($item.Serializer) | $($item.Scaffold) | $($item.Build) | $($item.Runtime) | $errorText |")
}

$report | Set-Content -LiteralPath $reportPath -Encoding UTF8

Write-Banner "Results"
$results | Format-Table Engine, Transport, Serializer, Scaffold, Build, Runtime -AutoSize
Write-Host ""
Write-Host "Report: $reportPath"
Write-Host "Summary: $summaryPath"
Write-Host "Passed: $passCount | Failed: $failCount"

if (-not $KeepScaffolds) {
    foreach ($item in $results) {
        if ($item.Scaffold -eq "PASS" -and $item.Build -eq "PASS" -and ($item.Runtime -eq "PASS" -or $item.Runtime -eq "SKIP")) {
            Remove-Item -LiteralPath $item.ProjectDir -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

if ($failCount -gt 0) {
    exit 1
}
