#Requires -Version 7.0
<#
.SYNOPSIS
    Unified E2E validation for Lakona.Tool scaffolded projects.

.DESCRIPTION
    Scaffolds, builds, and runtime-verifies Lakona.Tool generated projects
    with three dependency modes:

    ProjectReference — fastest dev feedback. Patches scaffolded csproj to
      reference local source directly. No NuGet packing needed.

    LocalFeed — pre-publish validation. Packs current src/Lakona.* projects
      into a local NuGet feed and validates the generated project against
      those packages. Simulates what users get after publishing.

    NuGetOrg — post-publish verification. Uses published packages from
      nuget.org (no local feed). Validates the real user experience.
#>

[CmdletBinding()]
param(
    [ValidateSet("ProjectReference", "LocalFeed", "NuGetOrg")]
    [string]$Feed = "ProjectReference",

    [ValidateSet("all", "unity", "tuanjie", "godot")]
    [string]$Engine = "godot",

    [ValidateSet("all", "tcp", "kcp", "websocket")]
    [string]$Transport = "websocket",

    [ValidateSet("all", "json", "memorypack")]
    [string]$Serializer = "memorypack",

    [switch]$SkipRuntime,

    [switch]$KeepScaffolds,

    [int]$Port = 20000,

    [string]$WorkDir = ".tmp/lakona-e2e"
)

$ErrorActionPreference = "Stop"
$runRuntime = -not $SkipRuntime

# ═══════════════════════════════════════════════════════════════════════════════
# Helper Functions
# ═══════════════════════════════════════════════════════════════════════════════

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
        "tcp" { "new TcpTransport(`"127.0.0.1`", $Port)" }
        "kcp" { "new KcpTransport(`"127.0.0.1`", $Port)" }
        "websocket" { "new WsTransport(`"ws://127.0.0.1:$Port/ws`")" }
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

function Get-PackageVersionFromCsproj {
    param(
        [string]$CsprojPath,
        [string]$PackageId
    )

    [xml]$xml = Get-Content -LiteralPath $CsprojPath -Raw
    $ns = @{ msbuild = "http://schemas.microsoft.com/developer/msbuild/2003" }

    $ref = $xml.SelectSingleNode(
        "//msbuild:PackageReference[@Include='$PackageId']",
        $ns
    )

    if ($ref) {
        $version = $ref.GetAttribute("Version")
        if ($version) {
            return $version
        }
    }

    throw "Could not find PackageReference for $PackageId in $CsprojPath"
}

function Write-NuGetConfig {
    param(
        [string]$Path,
        [string]$FeedDir,
        [bool]$IncludeNuGetOrg = $true
    )

    $escapedFeed = if ($FeedDir) {
        [System.Security.SecurityElement]::Escape($FeedDir)
    } else {
        ""
    }

    $lines = @('<?xml version="1.0" encoding="utf-8"?>')
    $lines += '<configuration>'
    $lines += '  <packageSources>'
    $lines += '    <clear />'

    if ($FeedDir) {
        $lines += "    <add key=`"local-lakona`" value=`"$escapedFeed`" />"
    }

    if ($IncludeNuGetOrg) {
        $lines += '    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />'
    }

    $lines += '  </packageSources>'
    $lines += '</configuration>'

    $lines -join "`n" | Set-Content -LiteralPath $Path -Encoding UTF8
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

    $config = Get-Content -LiteralPath $appSettings -Raw | ConvertFrom-Json
    $endpoints = @($config.Lakona.Endpoints)
    if ($endpoints.Count -eq 0) {
        throw "Generated server configuration has no Lakona:Endpoints entry: $appSettings"
    }

    foreach ($endpoint in $endpoints) {
        $endpoint.Port = $Port
    }

    $config | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $appSettings -Encoding UTF8
}

function Test-PortAvailable {
    param([int]$Port)

    try {
        $tcpListener = Get-NetTCPConnection `
            -State Listen `
            -LocalPort $Port `
            -ErrorAction SilentlyContinue
        $udpEndpoint = Get-NetUDPEndpoint `
            -LocalPort $Port `
            -ErrorAction SilentlyContinue

        return -not $tcpListener -and -not $udpEndpoint
    } catch {
        # The networking cmdlets are Windows-specific. On other platforms the
        # server bind remains the authoritative availability check.
        return $true
    }
}

function Stop-ProcessTree {
    param([System.Diagnostics.Process]$Process)

    if ($null -eq $Process -or $Process.HasExited) {
        return
    }

    try {
        $Process.Kill($true)
        $Process.WaitForExit(5000) | Out-Null
    } catch {
        Stop-Process -Id $Process.Id -Force -ErrorAction SilentlyContinue
    }
}

# ═══════════════════════════════════════════════════════════════════════════════
# E2E Client Generation
# ═══════════════════════════════════════════════════════════════════════════════

function New-E2EClient {
    param(
        [string]$ProjectDir,
        [string]$Transport,
        [string]$Serializer,
        [int]$Port,
        [string]$Feed,
        [string]$FeedDir,
        [string]$RepoRoot
    )

    $e2eDir = Join-Path $ProjectDir "E2EVerification"
    New-Item -ItemType Directory -Force -Path $e2eDir | Out-Null

    $sharedProj = (Resolve-Path (Join-Path $ProjectDir "Shared/Shared.csproj")).Path
    $transportPkg = Get-TransportPackageName $Transport
    $serializerPkg = Get-SerializerPackageName $Serializer

    # Build the csproj based on feed mode
    $csprojContent = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <LakonaRpcGenerateClient>true</LakonaRpcGenerateClient>
    <LakonaGameGenerateClient>true</LakonaGameGenerateClient>
  </PropertyGroup>
"@

    if ($Feed -eq "ProjectReference") {
        # ProjectReference mode must model compiler wiring that package mode
        # receives from Lakona.Rpc.Core's buildTransitive assets.
        $csprojContent += @"

  <ItemGroup>
    <CompilerVisibleProperty Include="LakonaRpcGenerateClient" />
    <CompilerVisibleProperty Include="LakonaGameGenerateClient" />
    <ProjectReference Include="$RepoRoot\src\Lakona.Game.Client\Lakona.Game.Client.csproj" />
    <ProjectReference Include="$RepoRoot\src\$transportPkg\$transportPkg.csproj" />
    <ProjectReference Include="$RepoRoot\src\$serializerPkg\$serializerPkg.csproj" />
    <ProjectReference Include="$RepoRoot\src\Lakona.Rpc.Analyzers\Lakona.Rpc.Analyzers.csproj"
                      OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
    <ProjectReference Include="$sharedProj" />
  </ItemGroup>
"@
    } else {
        # PackageReference mode (LocalFeed or NuGetOrg)
        # Resolve versions
        if ($Feed -eq "LocalFeed") {
            $gameClientVersion = Get-LocalPackageVersion -FeedDir $FeedDir -PackageId "Lakona.Game.Client"
            $transportVersion = Get-LocalPackageVersion -FeedDir $FeedDir -PackageId $transportPkg
            $serializerVersion = Get-LocalPackageVersion -FeedDir $FeedDir -PackageId $serializerPkg
        } else {
            # NuGetOrg: read versions from scaffolded Server.App.csproj
            $serverCsproj = Join-Path $ProjectDir "Server/App/Server.App.csproj"
            $gameClientVersion = Get-PackageVersionFromCsproj -CsprojPath $serverCsproj -PackageId "Lakona.Game.Client"
            $transportVersion = Get-PackageVersionFromCsproj -CsprojPath $serverCsproj -PackageId $transportPkg
            $serializerVersion = Get-PackageVersionFromCsproj -CsprojPath $serverCsproj -PackageId $serializerPkg
        }

        $csprojContent += @"

  <ItemGroup>
    <PackageReference Include="$transportPkg" Version="$transportVersion" />
    <PackageReference Include="$serializerPkg" Version="$serializerVersion" />
    <PackageReference Include="Lakona.Game.Client" Version="$gameClientVersion" />
    <ProjectReference Include="$sharedProj" />
  </ItemGroup>
"@
    }

    $csprojContent += @"
</Project>
"@

    Set-Content -LiteralPath (Join-Path $e2eDir "E2EVerification.csproj") -Value $csprojContent -Encoding UTF8

    # Write NuGet.config for package-based modes
    if ($Feed -ne "ProjectReference") {
        if ($Feed -eq "LocalFeed") {
            Write-NuGetConfig -Path (Join-Path $e2eDir "NuGet.config") -FeedDir $FeedDir -IncludeNuGetOrg $true
        } else {
            # NuGetOrg: only nuget.org source
            Write-NuGetConfig -Path (Join-Path $e2eDir "NuGet.config") -FeedDir $null -IncludeNuGetOrg $true
        }
    }

    # Generate Program.cs (same LakonaGameClient approach for all modes)
    $transportUsing = Get-TransportUsing $Transport
    $serializerUsing = Get-SerializerUsing $Serializer
    $transportCtor = Get-TransportConstructor $Transport $Port
    $serializerCtor = Get-SerializerConstructor $Serializer

    $program = @"
using Client.Generated;
using Shared.Contracts.Game;
using Lakona.Game.Client;
$transportUsing
$serializerUsing

try
{
    var transport = $transportCtor;
    var serializer = $serializerCtor;
    var options = new LakonaGameClientOptions(transport, serializer);
    var callbacks = new E2ECallbacks();
    await using var client = new LakonaGameClient(options, callbacks);

    Console.WriteLine("[E2E] Connecting to server...");
    await client.ConnectAsync();
    Console.WriteLine("[E2E] Connected.");

    var reply = await client.Api.Shared.Game.LoginAsync(
        new LoginRequest { PlayerName = "E2ETest" });
    var pushedWorld = await callbacks.WaitForWorldAsync(TimeSpan.FromSeconds(3));

    Console.WriteLine("[E2E] Success={0}, PlayerId={1}, LoginTick={2}, PushTick={3}", reply.Success, reply.PlayerId, reply.World.Tick, pushedWorld.Tick);
    if (reply.Success && reply.PlayerId > 0 &&
        reply.World.Players.Exists(player => player.PlayerId == reply.PlayerId && player.Name == "E2ETest") &&
        pushedWorld.Tick > reply.World.Tick)
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

internal sealed class E2ECallbacks : IGameCallback
{
    private readonly TaskCompletionSource<WorldSnapshot> _world = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void OnWorldUpdated(WorldSnapshot snapshot)
    {
        _world.TrySetResult(snapshot);
    }

    public Task<WorldSnapshot> WaitForWorldAsync(TimeSpan timeout) => _world.Task.WaitAsync(timeout);
}
"@

    Set-Content -LiteralPath (Join-Path $e2eDir "Program.cs") -Value $program -Encoding UTF8
    return $e2eDir
}

# ═══════════════════════════════════════════════════════════════════════════════
# Server Dependency Patching (ProjectReference mode)
# ═══════════════════════════════════════════════════════════════════════════════

function Patch-ServerDependencies {
    param(
        [string]$ProjectDir,
        [string]$RepoRoot
    )

    # Patch the shared contract project and all server projects that may
    # reference Lakona packages.
    $projectFiles = @(
        Get-Item -LiteralPath (Join-Path $ProjectDir "Shared/Shared.csproj")
        Get-ChildItem -Path (Join-Path $ProjectDir "Server") -Recurse -Filter "*.csproj"
    )

    foreach ($csprojPath in $projectFiles) {
        $content = Get-Content $csprojPath -Raw -Encoding UTF8
        $modified = $false

        # Single pass: match all Lakona.* PackageReference elements,
        # both self-closing and those with child elements.
        $pattern = '(?s)<PackageReference\s+Include="(Lakona\.[^"]+)"\s+Version="[^"]*".*?(?:/>|</PackageReference>)'

        $matches = [regex]::Matches($content, $pattern)
        $replacements = @()
        foreach ($match in $matches) {
            $packageId = $match.Groups[1].Value
            $projectPath = Join-Path $RepoRoot "src/$packageId/$packageId.csproj"
            if (Test-Path $projectPath) {
                $replacements += [PSCustomObject]@{
                    Index = $match.Index
                    Length = $match.Length
                    OldValue = $match.Value
                    PackageId = $packageId
                }
            }
        }

        # Sort by index descending so we can replace without invalidating positions
        $replacements = $replacements | Sort-Object Index -Descending

        foreach ($r in $replacements) {
            $projectPath = Join-Path $RepoRoot "src/$($r.PackageId)/$($r.PackageId).csproj"
            $replacement = '<ProjectReference Include="' + $projectPath + '" />'
            $content = $content.Substring(0, $r.Index) + $replacement + $content.Substring($r.Index + $r.Length)
            $modified = $true
            Write-Host "    $($r.PackageId) -> ProjectReference" -ForegroundColor DarkGray
        }

        # Analyzer ProjectReferences do not flow transitively. Package mode gets
        # the RPC analyzer from Lakona.Rpc.Core, while source mode must attach
        # the internal analyzer project directly to projects that generate RPC.
        if ($content.Contains("<LakonaRpcGenerateServer>true</LakonaRpcGenerateServer>") -and
            -not $content.Contains("Lakona.Rpc.Analyzers.csproj")) {
            $analyzerProjectPath = Join-Path $RepoRoot "src/Lakona.Rpc.Analyzers/Lakona.Rpc.Analyzers.csproj"
            $analyzerReference = @"

  <ItemGroup>
    <ProjectReference Include="$analyzerProjectPath" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
  </ItemGroup>
"@
            $content = $content.Replace("</Project>", "$analyzerReference`n</Project>")
            $modified = $true
            Write-Host "    Lakona.Rpc.Analyzers -> ProjectReference (analyzer)" -ForegroundColor DarkGray
        }

        # Package mode gets the Hotfix generator from
        # Lakona.Game.Server.Hotfix.Abstractions. ProjectReference mode must
        # attach the internal compiler project directly.
        if (($content.Contains("<LakonaHotfixGenerateStableRpcServices>") -or
             $content.Contains("<LakonaHotfixProject>true</LakonaHotfixProject>")) -and
            -not $content.Contains("Lakona.Game.Server.Hotfix.Generators.csproj")) {
            $hotfixAnalyzerProjectPath = Join-Path $RepoRoot "src/Lakona.Game.Server.Hotfix.Generators/Lakona.Game.Server.Hotfix.Generators.csproj"
            $hotfixAnalyzerReference = @"

  <ItemGroup>
    <ProjectReference Include="$hotfixAnalyzerProjectPath" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
  </ItemGroup>
"@
            $content = $content.Replace("</Project>", "$hotfixAnalyzerReference`n</Project>")
            $modified = $true
            Write-Host "    Lakona.Game.Server.Hotfix.Generators -> ProjectReference (analyzer)" -ForegroundColor DarkGray
        }

        # Lakona.Game.Server owns and bundles Hotfix.Abstractions in package
        # mode. A direct ProjectReference intentionally keeps that internal
        # project private, so this source-mode adapter must mirror the bundled
        # compile reference for projects that consume Game.Server source.
        $serverProjectPath = Join-Path $RepoRoot "src/Lakona.Game.Server/Lakona.Game.Server.csproj"
        if ($content.Contains($serverProjectPath) -and
            -not $content.Contains("Lakona.Game.Server.Hotfix.Abstractions.csproj")) {
            $hotfixAbstractionsProjectPath = Join-Path $RepoRoot "src/Lakona.Game.Server.Hotfix.Abstractions/Lakona.Game.Server.Hotfix.Abstractions.csproj"
            $hotfixAbstractionsReference = @"

  <ItemGroup>
    <ProjectReference Include="$hotfixAbstractionsProjectPath" />
  </ItemGroup>
"@
            $content = $content.Replace("</Project>", "$hotfixAbstractionsReference`n</Project>")
            $modified = $true
            Write-Host "    Lakona.Game.Server.Hotfix.Abstractions -> ProjectReference" -ForegroundColor DarkGray
        }

        # buildTransitive assets do not flow through ProjectReference. Mirror
        # only the compiler-property wiring required by this source-mode
        # adapter, while generated package-mode projects stay free of it.
        $compilerVisibleProperties = @(
            "LakonaRpcGenerateServer"
            "LakonaRpcServerGeneratedNamespace"
            "LakonaHotfixGenerateStableRpcServices"
            "LakonaHotfixProject"
        ) | Where-Object {
            $content.Contains("<$_>") -and
            -not $content.Contains("<CompilerVisibleProperty Include=`"$_`" />")
        }

        if ($compilerVisibleProperties.Count -gt 0) {
            $compilerVisibleItems = ($compilerVisibleProperties | ForEach-Object {
                "    <CompilerVisibleProperty Include=`"$_`" />"
            }) -join "`n"
            $compilerVisibleGroup = @"

  <ItemGroup>
$compilerVisibleItems
  </ItemGroup>
"@
            $content = $content.Replace("</Project>", "$compilerVisibleGroup`n</Project>")
            $modified = $true
            Write-Host "    CompilerVisibleProperty wiring -> source-mode adapter" -ForegroundColor DarkGray
        }

        if ($modified) {
            Set-Content -Path $csprojPath -Value $content -Encoding UTF8 -NoNewline
        }
    }

    Write-Host "  Patched shared and server csproj files for ProjectReference mode" -ForegroundColor DarkGray
}

# ═══════════════════════════════════════════════════════════════════════════════
# Main
# ═══════════════════════════════════════════════════════════════════════════════

$repoRoot = Resolve-RepoRoot
Set-Location $repoRoot

$workRoot = Join-Path $repoRoot $WorkDir
$feedDir = Join-Path $workRoot "feed"
$packageCache = Join-Path $workRoot "packages"
# Use relative paths for scaffold output to avoid Windows file-locking issues
# in TransactionalOutputWriter when passing absolute paths to --output.
$scaffoldRootRelative = Join-Path $WorkDir "scaffolds"
$scaffoldRoot = Join-Path $repoRoot $scaffoldRootRelative
$logRoot = Join-Path $workRoot "logs"
$reportPath = Join-Path $workRoot "report.md"
$summaryPath = Join-Path $workRoot "summary.json"

if ($Feed -eq "LocalFeed" -and (Test-Path $feedDir)) {
    Remove-Item -LiteralPath $feedDir -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $feedDir, $packageCache, $scaffoldRoot, $logRoot | Out-Null

$env:DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER = "1"

# Godot mock packages workaround (prevents SDK resolution from scanning C:\Program Files)
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$godotNupkgsPath = Join-Path $repoRoot "scripts/game/ci/mock-godot-nupkgs"
if (Test-Path $godotNupkgsPath) {
    $env:LAKONA_RPC_GODOT_NUPKGS = (Resolve-Path $godotNupkgsPath).Path
}

$engines = if ($Engine -eq "all") { @("unity", "tuanjie", "godot") } else { @($Engine) }
$transports = if ($Transport -eq "all") { @("tcp", "kcp", "websocket") } else { @($Transport) }
$serializers = if ($Serializer -eq "all") { @("json", "memorypack") } else { @($Serializer) }

# ═══════════════════════════════════════════════════════════════════════════════
# Step 1: Pack local packages (LocalFeed mode only)
# ═══════════════════════════════════════════════════════════════════════════════

if ($Feed -eq "LocalFeed") {
    Write-Banner "[$Feed] Packing local Lakona packages"
    $env:NUGET_PACKAGES = $packageCache

    $packageProjects = Get-ChildItem -Path (Join-Path $repoRoot "src") -Recurse -Filter "Lakona.*.csproj" |
        Where-Object {
            [xml] $projectXml = Get-Content -LiteralPath $_.FullName -Raw
            $isPackable = @($projectXml.Project.PropertyGroup.IsPackable) | Select-Object -Last 1
            if ($null -eq $isPackable) {
                return $true
            }

            -not [string]::Equals(
                ([string] $isPackable).Trim(),
                "false",
                [System.StringComparison]::OrdinalIgnoreCase)
        } |
        Sort-Object FullName

    $packSolution = Join-Path $workRoot "Lakona.LocalFeed.slnx"
    $solutionLines = [System.Collections.Generic.List[string]]::new()
    $solutionLines.Add("<Solution>")
    foreach ($project in $packageProjects) {
        $relativeProjectPath = [System.IO.Path]::GetRelativePath(
            $workRoot,
            $project.FullName).Replace("\", "/")
        $escapedProjectPath = [System.Security.SecurityElement]::Escape($relativeProjectPath)
        $solutionLines.Add("  <Project Path=`"$escapedProjectPath`" />")
    }
    $solutionLines.Add("</Solution>")
    $solutionLines | Set-Content -LiteralPath $packSolution -Encoding UTF8

    Write-Host "  Packing $($packageProjects.Count) projects in one MSBuild graph..." -ForegroundColor DarkGray
    dotnet pack $packSolution -c Release -o $feedDir --nologo -v q
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet pack failed for $packSolution."
    }

    $packages = @(Get-ChildItem -Path $feedDir -Filter "*.nupkg" -File |
        Where-Object { -not $_.Name.EndsWith(".snupkg", [System.StringComparison]::OrdinalIgnoreCase) })
    if ($packages.Count -ne $packageProjects.Count) {
        throw "Expected $($packageProjects.Count) local packages but found $($packages.Count) in $feedDir."
    }
}

# ═══════════════════════════════════════════════════════════════════════════════
# Step 2: Build Lakona.Tool
# ═══════════════════════════════════════════════════════════════════════════════

Write-Banner "[$Feed] Building Lakona.Tool"
dotnet build (Join-Path $repoRoot "src/Lakona.Tool/Lakona.Tool.csproj") --nologo -v q
if ($LASTEXITCODE -ne 0) {
    throw "Lakona.Tool build failed."
}

# ═══════════════════════════════════════════════════════════════════════════════
# Step 3: Test matrix
# ═══════════════════════════════════════════════════════════════════════════════

$results = New-Object System.Collections.Generic.List[object]
$total = $engines.Count * $transports.Count * $serializers.Count
$index = 0

if ($Port -lt 1 -or ($Port + $total - 1) -gt 65535) {
    throw "The base port $Port cannot provide $total consecutive matrix ports in the valid range 1-65535."
}

foreach ($engineValue in $engines) {
    foreach ($transportValue in $transports) {
        foreach ($serializerValue in $serializers) {
            $index++
            $casePort = $Port + $index - 1
            $modeLabel = $Feed.Substring(0,1).ToUpperInvariant() + $Feed.Substring(1).ToLowerInvariant()
            $projectName = "E2E_${modeLabel}_${engineValue}_${transportValue}_${serializerValue}" -replace "[^A-Za-z0-9_]", "_"
            $projectDir = Join-Path $scaffoldRoot $projectName
            $label = "[$index/$total] $Feed / $engineValue / $transportValue / $serializerValue"

            Write-Banner $label

            $result = [ordered]@{
                Engine = $engineValue
                Transport = $transportValue
                Serializer = $serializerValue
                Feed = $Feed
                Port = $casePort
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

                # ── 3a. Scaffold ────────────────────────────────────────
                Write-Host "  Scaffolding..." -ForegroundColor Yellow
                $scaffoldResult = $null
                $scaffoldAttempt = 0
                $scaffoldMaxAttempts = 3
                do {
                    $scaffoldAttempt++
                    if ($scaffoldAttempt -gt 1) {
                        Write-Host "  Retry $scaffoldAttempt/$scaffoldMaxAttempts after file-lock delay..." -ForegroundColor DarkYellow
                        Start-Sleep -Seconds 3
                        # Clean up any leftover staging directory
                        $stagingPattern = Join-Path $scaffoldRoot ".${projectName}.tmp-*"
                        Remove-Item -Path $stagingPattern -Recurse -Force -ErrorAction SilentlyContinue
                    }
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
                            "--nugetforunity-source", "embedded",
                            "--deploy-profile", "none",
                            "--output", $scaffoldRootRelative)
                } while ($scaffoldResult.ExitCode -ne 0 -and $scaffoldAttempt -lt $scaffoldMaxAttempts)

                if ($scaffoldResult.ExitCode -ne 0) {
                    $result.Error = "Scaffold failed."
                    $result.ErrorDetail = $scaffoldResult.Tail
                    $result.LogPath = $scaffoldResult.LogPath
                    $results.Add([pscustomobject]$result)
                    continue
                }
                $result.Scaffold = "PASS"
                Write-Host "  Scaffold: OK" -ForegroundColor Green

                # ── 3b. Resolve dependencies ────────────────────────────
                switch ($Feed) {
                    "ProjectReference" {
                        Patch-ServerDependencies -ProjectDir $projectDir `
                            -RepoRoot $repoRoot
                    }
                    "LocalFeed" {
                        Write-NuGetConfig -Path (Join-Path $projectDir "NuGet.config") `
                            -FeedDir $feedDir -IncludeNuGetOrg $true
                        Write-Host "  NuGet.config written for local feed" -ForegroundColor DarkGray
                    }
                    "NuGetOrg" {
                        # No NuGet.config needed — uses default nuget.org source
                        Write-Host "  Using nuget.org packages (no local feed)" -ForegroundColor DarkGray
                    }
                }

                Set-GeneratedServerPort $projectDir $casePort

                # Verify scaffold output
                $serverSln = Join-Path $projectDir "Server/Server.slnx"
                if (-not (Test-Path $serverSln)) {
                    $result.Error = "Server.slnx not found after scaffold."
                    $results.Add([pscustomobject]$result)
                    continue
                }

                # ── 3c. Build server ────────────────────────────────────
                Write-Host "  Building server..." -ForegroundColor Yellow
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
                Write-Host "  Build: OK" -ForegroundColor Green

                # ── 3d. Runtime verification ────────────────────────────
                if (-not $runRuntime) {
                    $results.Add([pscustomobject]$result)
                    continue
                }

                if (-not (Test-PortAvailable $casePort)) {
                    $result.Error = "Port $casePort already has a TCP listener or UDP endpoint."
                    $results.Add([pscustomobject]$result)
                    continue
                }

                # Generate E2E verification client
                $e2eDir = New-E2EClient -ProjectDir $projectDir `
                    -Transport $transportValue `
                    -Serializer $serializerValue `
                    -Port $casePort `
                    -Feed $Feed `
                    -FeedDir $feedDir `
                    -RepoRoot $repoRoot

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

                # Start server
                Write-Host "  Starting server..." -ForegroundColor Yellow
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
                        if ($serverText -match "Lakona server started successfully" -or
                            $serverText -match "Application started") {
                            $ready = $true
                            Write-Host "  Server ready (waited $i seconds)." -ForegroundColor Green
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

                # Run E2E client
                Write-Host "  Running E2E verification..." -ForegroundColor Yellow
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
                Write-Host "  E2E: PASS" -ForegroundColor Green
                $results.Add([pscustomobject]$result)
            } catch {
                $result.Error = $_.Exception.Message
                $result.ErrorDetail = $_.Exception.ToString()
                $results.Add([pscustomobject]$result)
            } finally {
                Stop-ProcessTree $serverProc
            }
        }
    }
}

# ═══════════════════════════════════════════════════════════════════════════════
# Report
# ═══════════════════════════════════════════════════════════════════════════════

$passCount = ($results | Where-Object {
    $_.Scaffold -eq "PASS" -and $_.Build -eq "PASS" -and ($_.Runtime -eq "PASS" -or $_.Runtime -eq "SKIP")
}).Count
$failCount = $results.Count - $passCount

$results | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $summaryPath -Encoding UTF8

$report = New-Object System.Collections.Generic.List[string]
$report.Add("# Lakona E2E Report ($Feed)")
$report.Add("")
$report.Add("- Generated at: $([DateTimeOffset]::UtcNow.ToString("u"))")
$report.Add("- Feed mode: $Feed")
$report.Add("- Runtime verification: $runRuntime")
$report.Add("- Work directory: $workRoot")
$report.Add("- Logs: $logRoot")
if ($Feed -eq "LocalFeed") {
    $report.Add("- Local feed: $feedDir")
    $report.Add("- Isolated package cache: $packageCache")
}
$report.Add("- Passed: $passCount")
$report.Add("- Failed: $failCount")
$report.Add("")
$report.Add("| Engine | Transport | Serializer | Feed | Port | Scaffold | Build | Runtime | Error | Details | Log |")
$report.Add("| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |")
foreach ($item in $results) {
    $errorText = Format-ReportCell $item.Error
    $detailText = Format-ReportCell $item.ErrorDetail
    $logText = Format-ReportCell $item.LogPath
    $report.Add("| $($item.Engine) | $($item.Transport) | $($item.Serializer) | $($item.Feed) | $($item.Port) | $($item.Scaffold) | $($item.Build) | $($item.Runtime) | $errorText | $detailText | $logText |")
}

$report | Set-Content -LiteralPath $reportPath -Encoding UTF8

Write-Banner "Results"
$results | Format-Table Engine, Transport, Serializer, Port, Scaffold, Build, Runtime -AutoSize
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
