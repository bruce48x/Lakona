#Requires -Version 5.1
<#
.SYNOPSIS
    Validates Lakona.Tool generated projects against locally packed Lakona NuGet packages.

.DESCRIPTION
    Packs current src/Lakona.* projects into a local feed, scaffolds generated
    projects with Lakona.Tool, builds the generated server, and runs a runtime
    RPC verification client by default.
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

    [switch]$SkipRuntime,

    [switch]$KeepScaffolds,

    [int]$Port = 20000,

    [string]$WorkDir = ".tmp/lakona-local-package-e2e"
)

$ErrorActionPreference = "Stop"
$runRuntime = -not $SkipRuntime
if ($Runtime) {
    $runRuntime = $true
}

function Write-Banner {
    param([string]$Text)

    Write-Host ""
    Write-Host ("=" * 72) -ForegroundColor Cyan
    Write-Host "  $Text" -ForegroundColor Cyan
    Write-Host ("=" * 72) -ForegroundColor Cyan
}

function Format-ReportCell {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return ""
    }

    return ($Value -replace "`r?`n", "<br>" -replace "\|", "\|")
}

function Invoke-LoggedNativeCommand {
    param(
        [string]$LogPath,
        [string]$FilePath,
        [string[]]$ArgumentList
    )

    $logDir = Split-Path -Parent $LogPath
    if (-not [string]::IsNullOrWhiteSpace($logDir)) {
        New-Item -ItemType Directory -Force -Path $logDir | Out-Null
    }

    $output = & $FilePath @ArgumentList 2>&1
    $exitCode = $LASTEXITCODE
    $text = ($output | Out-String).TrimEnd()
    Set-Content -LiteralPath $LogPath -Value $text -Encoding UTF8

    if (-not [string]::IsNullOrWhiteSpace($text)) {
        Write-Host $text
    }

    $lines = $text -split "`r?`n"
    $tail = ($lines | Select-Object -Last 40) -join "`n"

    return [pscustomobject]@{
        ExitCode = $exitCode
        Output = $text
        Tail = $tail
        LogPath = $LogPath
    }
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
    $rpcAnalyzersVersion = Get-LocalPackageVersion $FeedDir "Lakona.Rpc.Analyzers"
    $gameClientVersion = Get-LocalPackageVersion $FeedDir "Lakona.Game.Client"
    $gameAbstractionsVersion = Get-LocalPackageVersion $FeedDir "Lakona.Game.Abstractions"
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
    <LakonaRpcGenerateClient>true</LakonaRpcGenerateClient>
    <LakonaRpcGeneratedNamespace>Rpc.Generated</LakonaRpcGeneratedNamespace>
    <LakonaGameGenerateClient>true</LakonaGameGenerateClient>
    <LakonaGameClientRuntime>dotnet-client</LakonaGameClientRuntime>
    <LakonaGameClientPlatform>local-e2e</LakonaGameClientPlatform>
    <LakonaGameClientGameVersion>local-package-e2e</LakonaGameClientGameVersion>
  </PropertyGroup>
  <ItemGroup>
    <CompilerVisibleProperty Include="LakonaRpcGenerateClient" />
    <CompilerVisibleProperty Include="LakonaRpcGeneratedNamespace" />
    <CompilerVisibleProperty Include="LakonaGameGenerateClient" />
    <CompilerVisibleProperty Include="LakonaGameClientRuntime" />
    <CompilerVisibleProperty Include="LakonaGameClientPlatform" />
    <CompilerVisibleProperty Include="LakonaGameClientGameVersion" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Lakona.Rpc.Core" Version="$rpcCoreVersion" />
    <PackageReference Include="Lakona.Rpc.Client" Version="$rpcClientVersion" />
    <PackageReference Include="$transportPackage" Version="$transportVersion" />
    <PackageReference Include="$serializerPackage" Version="$serializerVersion" />
    <PackageReference Include="Lakona.Rpc.Analyzers" Version="$rpcAnalyzersVersion">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
    <PackageReference Include="Lakona.Game.Client" Version="$gameClientVersion" />
    <PackageReference Include="Lakona.Game.Abstractions" Version="$gameAbstractionsVersion" />
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
using Rpc.Generated;
using Shared.Contracts.Chat;
using Lakona.Rpc.Client;
$transportUsing
$serializerUsing

try
{
    var transport = $transportCtor;
    var serializer = $serializerCtor;
    var options = new RpcClientOptions(transport, serializer);
    await using var client = new LakonaGameClient(options, new E2ECallbacks());

    Console.WriteLine("[E2E] Connecting to server...");
    await client.ConnectAsync();
    Console.WriteLine("[E2E] Connected.");

    var reply = await client.Api.Shared.Login.LoginAsync(
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

internal sealed class E2ECallbacks : ILoginCallback, IChatCallback
{
    public void OnUserJoined(ChatMember member)
    {
    }

    public void OnUserLeft(ChatUserLeft evt)
    {
    }

    public void OnMessageReceived(ChatMessage msg)
    {
    }
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
$logRoot = Join-Path $workRoot "logs"
$reportPath = Join-Path $workRoot "report.md"
$summaryPath = Join-Path $workRoot "summary.json"

New-Item -ItemType Directory -Force -Path $feedDir, $packageCache, $scaffoldRoot, $logRoot | Out-Null

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
                Runtime = if ($runRuntime) { "FAIL" } else { "SKIP" }
                ProjectDir = $projectDir
                Error = ""
                ErrorDetail = ""
                LogPath = ""
            }

            $serverProc = $null

            try {
                if (Test-Path $projectDir) {
                    Remove-Item -LiteralPath $projectDir -Recurse -Force
                }

                $caseLogPrefix = Join-Path $logRoot $projectName
                $scaffoldResult = Invoke-LoggedNativeCommand `
                    -LogPath "$caseLogPrefix.scaffold.log" `
                    -FilePath "dotnet" `
                    -ArgumentList @(
                        "run",
                        "--project", (Join-Path $repoRoot "src/Lakona.Tool"),
                        "--no-build",
                        "--",
                        "new",
                        "--name", $projectName,
                        "--client-engine", $engineValue,
                        "--transport", $transportValue,
                        "--serializer", $serializerValue,
                        "--persistence", "none",
                        "--nugetforunity-source", "embedded",
                        "--deploy-profile", "none",
                        "--output", $scaffoldRoot)

                if ($scaffoldResult.ExitCode -ne 0) {
                    $result.Error = "Scaffold failed."
                    $result.ErrorDetail = $scaffoldResult.Tail
                    $result.LogPath = $scaffoldResult.LogPath
                    $results.Add([pscustomobject]$result)
                    continue
                }

                $result.Scaffold = "PASS"
                Write-NuGetConfig (Join-Path $projectDir "NuGet.config") $feedDir
                Set-GeneratedServerPort $projectDir $Port

                $serverSln = Join-Path $projectDir "Server/Server.slnx"
                $buildResult = Invoke-LoggedNativeCommand `
                    -LogPath "$caseLogPrefix.server-build.log" `
                    -FilePath "dotnet" `
                    -ArgumentList @("build", $serverSln, "--nologo", "-v", "q")
                if ($buildResult.ExitCode -ne 0) {
                    $result.Error = "Generated server build failed."
                    $result.ErrorDetail = $buildResult.Tail
                    $result.LogPath = $buildResult.LogPath
                    $results.Add([pscustomobject]$result)
                    continue
                }

                $result.Build = "PASS"

                if ($runRuntime) {
                    if (-not (Test-PortFree $Port)) {
                        throw "Port $Port is already in use. Re-run with -Port <free-port>."
                    }

                    $e2eDir = New-E2EClient $projectDir $feedDir $transportValue $serializerValue $Port
                    $clientBuildResult = Invoke-LoggedNativeCommand `
                        -LogPath "$caseLogPrefix.e2e-client-build.log" `
                        -FilePath "dotnet" `
                        -ArgumentList @("build", (Join-Path $e2eDir "E2EVerification.csproj"), "--nologo", "-v", "q")
                    if ($clientBuildResult.ExitCode -ne 0) {
                        $result.Error = "E2E client build failed."
                        $result.ErrorDetail = $clientBuildResult.Tail
                        $result.LogPath = $clientBuildResult.LogPath
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
                        $result.ErrorDetail = "Server stdout: $serverOut`nServer stderr: $serverErr"
                        $result.LogPath = $serverErr
                        $results.Add([pscustomobject]$result)
                        continue
                    }

                    $clientRunResult = Invoke-LoggedNativeCommand `
                        -LogPath "$caseLogPrefix.e2e-client-run.log" `
                        -FilePath "dotnet" `
                        -ArgumentList @("run", "--project", (Join-Path $e2eDir "E2EVerification.csproj"), "--no-build")
                    if ($clientRunResult.ExitCode -ne 0) {
                        $result.Error = "Runtime E2E client failed."
                        $result.ErrorDetail = $clientRunResult.Tail
                        $result.LogPath = $clientRunResult.LogPath
                        $results.Add([pscustomobject]$result)
                        continue
                    }

                    $result.Runtime = "PASS"
                }

                $results.Add([pscustomobject]$result)
            } catch {
                $result.Error = $_.Exception.Message
                $result.ErrorDetail = $_.Exception.ToString()
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
$report.Add("- Runtime verification: $([bool]$runRuntime)")
$report.Add("- Local feed: $feedDir")
$report.Add("- Isolated package cache: $packageCache")
$report.Add("- Logs: $logRoot")
$report.Add("- Passed: $passCount")
$report.Add("- Failed: $failCount")
$report.Add("")
$report.Add("| Engine | Transport | Serializer | Scaffold | Build | Runtime | Error | Details | Log |")
$report.Add("| --- | --- | --- | --- | --- | --- | --- | --- | --- |")
foreach ($item in $results) {
    $errorText = Format-ReportCell $item.Error
    $detailText = Format-ReportCell $item.ErrorDetail
    $logText = Format-ReportCell $item.LogPath
    $report.Add("| $($item.Engine) | $($item.Transport) | $($item.Serializer) | $($item.Scaffold) | $($item.Build) | $($item.Runtime) | $errorText | $detailText | $logText |")
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
