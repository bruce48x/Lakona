#Requires -Version 5.1
<#
.SYNOPSIS
    E2E test matrix for Lakona.Tool scaffolded projects.
    Tests all combinations of engine × transport × serializer.

.DESCRIPTION
    Scaffolds a Lakona game project, builds the server, starts it, sends real
    RPC requests from a generated E2E verification client, and verifies correct
    Login responses.

    Default mode runs all 12 combinations:
      unity/godot × tcp/kcp/websocket × json/memorypack

    Filter parameters let you test a subset. -Quick skips runtime verification.

.EXAMPLE
    # Run all 12 combinations with full E2E verification
    .\scripts\game\ci\test-lakona-tool-matrix.ps1

.EXAMPLE
    # Fast smoke test: only Godot + websocket, scaffold + build only
    .\scripts\game\ci\test-lakona-tool-matrix.ps1 -Engine godot -Transport websocket -Quick

.EXAMPLE
    # CI-like mode using packed local NuGet feed
    .\scripts\game\ci\test-lakona-tool-matrix.ps1 -DependencyMode NuGetFeed

.EXAMPLE
    # Single combination, keep artifacts
    .\scripts\game\ci\test-lakona-tool-matrix.ps1 -Engine unity -Transport kcp -Serializer memorypack -KeepScaffolds
#>

[CmdletBinding()]
param(
    [ValidateSet("all", "unity", "godot")]
    [string]$Engine = "all",

    [ValidateSet("all", "tcp", "kcp", "websocket")]
    [string]$Transport = "all",

    [ValidateSet("all", "json", "memorypack")]
    [string]$Serializer = "all",

    [ValidateSet("ProjectReference", "NuGetFeed")]
    [string]$DependencyMode = "ProjectReference",

    [switch]$Quick,

    [switch]$KeepScaffolds,

    [int]$Port = 20000
)

$ErrorActionPreference = "Stop"
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptRoot "../../..")

Set-Location $repoRoot

# ── Helpers ────────────────────────────────────────────────────────────────

function Write-Banner {
    param([string]$Text)
    $line = "=" * 60
    Write-Host ""
    Write-Host $line -ForegroundColor Cyan
    Write-Host "  $Text" -ForegroundColor Cyan
    Write-Host $line -ForegroundColor Cyan
}

function Get-TransportConstructor {
    param([string]$T)
    $T = $T.ToLowerInvariant()
    switch ($T) {
        "tcp"       { return "new TcpTransport(`"127.0.0.1`", $Port)" }
        "websocket" { return "new WsTransport(`"ws://127.0.0.1:$Port/ws`")" }
        "kcp"       { return "new KcpTransport(`"127.0.0.1`", $Port)" }
    }
}

function Get-SerializerConstructor {
    param([string]$S)
    $S = $S.ToLowerInvariant()
    switch ($S) {
        "json"       { return "new JsonRpcSerializer()" }
        "memorypack" { return "new MemoryPackRpcSerializer()" }
    }
}

function Get-TransportUsing {
    param([string]$T)
    $T = $T.ToLowerInvariant()
    switch ($T) {
        "tcp"       { return "using Lakona.Rpc.Transport.Tcp;" }
        "websocket" { return "using Lakona.Rpc.Transport.WebSocket;" }
        "kcp"       { return "using Lakona.Rpc.Transport.Kcp;" }
    }
}

function Get-SerializerUsing {
    param([string]$S)
    $S = $S.ToLowerInvariant()
    switch ($S) {
        "json"       { return "using Lakona.Rpc.Serializer.Json;" }
        "memorypack" { return "using Lakona.Rpc.Serializer.MemoryPack;" }
    }
}

function Get-TransportPackageName {
    param([string]$T)
    switch ($T.ToLowerInvariant()) {
        "tcp"       { return "Lakona.Rpc.Transport.Tcp" }
        "websocket" { return "Lakona.Rpc.Transport.WebSocket" }
        "kcp"       { return "Lakona.Rpc.Transport.Kcp" }
    }
}

function Get-SerializerPackageName {
    param([string]$S)
    switch ($S.ToLowerInvariant()) {
        "json"       { return "Lakona.Rpc.Serializer.Json" }
        "memorypack" { return "Lakona.Rpc.Serializer.MemoryPack" }
    }
}

# ── Matrix resolution ──────────────────────────────────────────────────────

$engines = if ($Engine -eq "all") { @("unity", "godot") } else { @($Engine) }
$transports = if ($Transport -eq "all") { @("tcp", "kcp", "websocket") } else { @($Transport) }
$serializerList = if ($Serializer -eq "all") { @("json", "memorypack") } else { @($Serializer) }

$totalTests = $engines.Count * $transports.Count * $serializerList.Count
$matrixDir = ".tmp/lakona-tool-matrix"

# Prevent MSBuild from blocking on file locks
$env:DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER = "1"

# Work around Godot SDK resolution bug (scans C:\Program Files without access handling)
$godotNupkgsPath = Join-Path $scriptRoot "mock-godot-nupkgs"
if (Test-Path $godotNupkgsPath) {
    $env:LAKONA_RPC_GODOT_NUPKGS = (Resolve-Path $godotNupkgsPath).Path
}

Write-Banner "Lakona.Tool E2E Matrix Test"
Write-Host "  Engines:     $($engines -join ', ')"
Write-Host "  Transports:  $($transports -join ', ')"
Write-Host "  Serializers: $($serializerList -join ', ')"
Write-Host "  Total:       $totalTests combination(s)"
Write-Host "  Mode:        $(if ($Quick) { 'Quick (scaffold+build only)' } else { 'Full E2E' })"
Write-Host "  Dependency:  $DependencyMode"
Write-Host "  Output:      $matrixDir/"

$results = @()
$testIndex = 0

# ── NuGetFeed mode: pack local packages ─────────────────────────────────────

if ($DependencyMode -eq "NuGetFeed") {
    $localFeed = Join-Path $matrixDir "ci-nuget"
    Write-Banner "Packing local NuGet packages"
    New-Item -ItemType Directory -Force -Path $localFeed | Out-Null

    $packageProjects = Get-ChildItem "$repoRoot/src" -Recurse -Filter "Lakona.*.csproj" `
        | Where-Object { $_.FullName -notmatch "Lakona.Tool" } `
        | ForEach-Object { $_.FullName }
    $packageProjects += (Join-Path $repoRoot "src/Lakona.Tool/Lakona.Tool.csproj")

    foreach ($proj in $packageProjects) {
        $name = [System.IO.Path]::GetFileNameWithoutExtension($proj)
        Write-Host "  Packing $name..." -ForegroundColor DarkGray
        dotnet pack $proj -c Release -o $localFeed --nologo -v q 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Failed to pack $proj" }
    }
    Write-Host "  Packages written to $localFeed" -ForegroundColor Green
}

# ── Build Lakona.Tool once ──────────────────────────────────────────────────

Write-Banner "Building Lakona.Tool"
dotnet build src/Lakona.Tool/Lakona.Tool.csproj --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "Tool build failed" }

# ── Main test loop ──────────────────────────────────────────────────────────

foreach ($engine in $engines) {
    foreach ($transport in $transports) {
        foreach ($ser in $serializerList) {
            $testIndex++
            $projectName = "E2E_${engine}_${transport}_${serializerS}"
            $scaffoldDir = Join-Path $matrixDir $projectName
            $label = "[$testIndex/$totalTests] $engine / $transport / $ser"

            Write-Banner $label

            $result = [PSCustomObject]@{
                Engine     = $engine
                Transport  = $transport
                Serializer = $ser
                Scaffold   = "FAIL"
                Build      = "FAIL"
                E2E        = if ($Quick) { "SKIP" } else { "FAIL" }
                Error      = ""
            }

            try {
                # ── 0. Ensure port is free ────────────────────────────────
                if (-not $Quick) {
                    try {
                        $portHolder = Get-NetTCPConnection -LocalPort $Port -ErrorAction SilentlyContinue |
                            Select-Object -ExpandProperty OwningProcess -Unique
                        if ($portHolder) {
                            foreach ($pid2 in $portHolder) {
                                Stop-Process -Id $pid2 -Force -ErrorAction SilentlyContinue
                            }
                            Write-Host "  Killed process(es) on port $Port" -ForegroundColor DarkGray
                            Start-Sleep -Seconds 2
                        }
                    } catch {
                        Write-Host "  Port check skipped (non-Windows or permission)" -ForegroundColor DarkGray
                    }
                }

                # ── 1. Scaffold ───────────────────────────────────────────
                Write-Host "  Scaffolding..." -ForegroundColor Yellow
                if (Test-Path $scaffoldDir) { Remove-Item -Recurse -Force $scaffoldDir }

                dotnet run --project src/Lakona.Tool --no-build -- `
                    new `
                    --name $projectName `
                    --client-engine $engine `
                    --transport $transport `
                    --serializer $ser `
                    --network-profile cluster `
                    --persistence none `
                    --nugetforunity-source embedded `
                    --deploy-profile none `
                    --output $matrixDir/ 2>&1 | Out-Null

                if ($LASTEXITCODE -ne 0) {
                    $result.Error = "Scaffold failed"
                    $results += $result
                    Write-Host "  FAIL: Scaffold" -ForegroundColor Red
                    continue
                }
                $result.Scaffold = "PASS"
                Write-Host "  Scaffold: OK" -ForegroundColor Green

                # ── 1b. Resolve dependencies ──────────────────────────────
                $serverCsproj = Join-Path $scaffoldDir "Server/App/Server.App.csproj"

                if ($DependencyMode -eq "NuGetFeed") {
                    # Write NuGet.config pointing to local feed
                    $nugetConfig = Join-Path $scaffoldDir "NuGet.config"
                    @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="$localFeed" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
"@ | Set-Content -Path $nugetConfig -Encoding UTF8
                    Write-Host "  NuGet.config written for local feed" -ForegroundColor DarkGray
                } else {
                    # ProjectReference mode: patch csproj
                    $transportPkg = Get-TransportPackageName $transport
                    $serializerPkg = Get-SerializerPackageName $ser
                    $csprojContent = Get-Content $serverCsproj -Raw -Encoding UTF8

                    # Replace transport PackageReference with ProjectReference
                    $transportProj = Join-Path $repoRoot "src/$transportPkg/$transportPkg.csproj"
                    $pat = '<PackageReference Include="' + $transportPkg + '" Version="[^"]*" */>'
                    $rep = '<ProjectReference Include="' + $transportProj + '" />'
                    $csprojContent = $csprojContent -replace $pat, $rep

                    # Replace/add serializer PackageReference
                    $serializerProj = Join-Path $repoRoot "src/$serializerPkg/$serializerPkg.csproj"
                    $serPat = '<PackageReference Include="' + $serializerPkg + '" Version="[^"]*" */>'
                    $serRep = '<ProjectReference Include="' + $serializerProj + '" />'
                    if ($csprojContent -match $serPat) {
                        $csprojContent = $csprojContent -replace $serPat, $serRep
                    } else {
                        $insert = '    <ProjectReference Include="' + $serializerProj + '" />'
                        $csprojContent = $csprojContent -replace `
                            '  </ItemGroup>\s*<ItemGroup>\s*<None Update="appsettings.json"',
                            "    $insert`n  </ItemGroup>`n  <ItemGroup>`n    <None Update=`"appsettings.json`""
                    }

                    Set-Content -Path $serverCsproj -Value $csprojContent -Encoding UTF8
                    Write-Host "  Patched csproj for ProjectReference mode" -ForegroundColor DarkGray
                }

                # Verify scaffold output
                $serverSlnx = Join-Path $scaffoldDir "Server/Server.slnx"
                if (-not (Test-Path $serverSlnx)) {
                    $result.Error = "Server.slnx not found"
                    $results += $result
                    Write-Host "  FAIL: Server.slnx missing" -ForegroundColor Red
                    continue
                }

                # ── 2. Build Server ───────────────────────────────────────
                Write-Host "  Building server..." -ForegroundColor Yellow
                dotnet build $serverSlnx --nologo -v q 2>&1 | Out-Null
                if ($LASTEXITCODE -ne 0) {
                    $result.Error = "Build failed"
                    $results += $result
                    Write-Host "  FAIL: Build" -ForegroundColor Red
                    continue
                }
                $result.Build = "PASS"
                Write-Host "  Build: OK" -ForegroundColor Green

                if ($Quick) {
                    $results += $result
                    Write-Host "  Quick mode: skipping E2E runtime verification" -ForegroundColor DarkGray
                    continue
                }

                # ── 3. Generate E2E verification client ───────────────────
                $e2eDir = Join-Path $scaffoldDir "E2EVerification"
                New-Item -ItemType Directory -Force -Path $e2eDir | Out-Null

                $sharedProj = (Resolve-Path (Join-Path $scaffoldDir "Shared/Shared.csproj")).Path
                $transportProjRef = Join-Path $repoRoot "src/$(Get-TransportPackageName $transport)/$(Get-TransportPackageName $transport).csproj"
                $serializerProjRef = Join-Path $repoRoot "src/$(Get-SerializerPackageName $ser)/$(Get-SerializerPackageName $ser).csproj"

@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="$transportProjRef" />
    <ProjectReference Include="$serializerProjRef" />
    <ProjectReference Include="$repoRoot\src\Lakona.Rpc.Core\Lakona.Rpc.Core.csproj" />
    <ProjectReference Include="$repoRoot\src\Lakona.Rpc.Client\Lakona.Rpc.Client.csproj" />
    <ProjectReference Include="$sharedProj" />
  </ItemGroup>
</Project>
"@ | Set-Content -Path (Join-Path $e2eDir "E2EVerification.csproj") -Encoding UTF8

                $transportUsing = Get-TransportUsing $transport
                $serializerUsing = Get-SerializerUsing $ser
                $transportCtor = Get-TransportConstructor $transport
                $serializerCtor = Get-SerializerConstructor $ser

@"
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

    // Login: ServiceId=1, MethodId=1
    var reply = await client.CallAsync(
        new RpcMethod<LoginRequest, LoginReply>(1, 1),
        new LoginRequest { PlayerName = "E2ETest" });

    Console.WriteLine("[E2E] Login reply received.");
    Console.WriteLine("[E2E] Members={0}, RecentMessages={1}", reply.Members.Count, reply.RecentMessages.Count);

    if (reply.Members.Count == 1 && reply.Members[0].Name == "E2ETest")
    {
        Console.WriteLine("[E2E] VERIFIED: Login response is correct.");
    }
    else
    {
        Console.Error.WriteLine("[E2E] FAIL: Unexpected login response.");
        Environment.Exit(1);
    }

    Console.WriteLine("[E2E] SUCCESS");
    Environment.Exit(0);
}
catch (Exception ex)
{
    Console.Error.WriteLine("[E2E] FAIL: {0}", ex);
    Environment.Exit(1);
}
"@ | Set-Content -Path (Join-Path $e2eDir "Program.cs") -Encoding UTF8

                # ── 4. Build E2E client ───────────────────────────────────
                Write-Host "  Building E2E client..." -ForegroundColor Yellow
                dotnet build (Join-Path $e2eDir "E2EVerification.csproj") --nologo -v q 2>&1 | Out-Null
                if ($LASTEXITCODE -ne 0) {
                    $result.Error = "E2E client build failed"
                    $results += $result
                    Write-Host "  FAIL: E2E client build" -ForegroundColor Red
                    continue
                }
                Write-Host "  E2E client build: OK" -ForegroundColor Green

                # ── 5. Start server ───────────────────────────────────────
                Write-Host "  Starting server..." -ForegroundColor Yellow
                $serverOut = Join-Path $scaffoldDir "server-out.txt"
                $serverErr = Join-Path $scaffoldDir "server-err.txt"
                $serverProject = Join-Path $scaffoldDir "Server/App/Server.App.csproj"

                $proc = Start-Process -FilePath "dotnet" `
                    -ArgumentList "run", "--project", $serverProject, "--no-build" `
                    -NoNewWindow `
                    -PassThru `
                    -RedirectStandardOutput $serverOut `
                    -RedirectStandardError $serverErr

                # Wait for server readiness
                $ready = $false
                for ($i = 0; $i -lt 30; $i++) {
                    Start-Sleep -Seconds 1
                    if (Test-Path $serverOut) {
                        $content = Get-Content $serverOut -ErrorAction SilentlyContinue | Out-String
                        if ($content -match "Application started|listening|Listening|RPC server") {
                            $ready = $true
                            Write-Host "  Server is ready (waited $i seconds)." -ForegroundColor Green
                            break
                        }
                    }
                    if ($proc.HasExited) {
                        $errContent = Get-Content $serverErr -ErrorAction SilentlyContinue | Out-String
                        Write-Host "  Server exited early! Stderr:" -ForegroundColor Red
                        Write-Host $errContent
                        break
                    }
                }

                if (-not $ready) {
                    $result.Error = "Server did not start in time"
                    $results += $result
                    Write-Host "  FAIL: Server startup" -ForegroundColor Red
                    if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force }
                    continue
                }

                # ── 6. Run E2E client ─────────────────────────────────────
                Write-Host "  Running E2E verification..." -ForegroundColor Yellow
                $clientOut = & dotnet run --project (Join-Path $e2eDir "E2EVerification.csproj") --no-build 2>&1
                $clientExit = $LASTEXITCODE
                Write-Host ($clientOut -join "`n")

                if ($clientExit -eq 0) {
                    $result.E2E = "PASS"
                    Write-Host "  E2E: PASS" -ForegroundColor Green
                } else {
                    $result.Error = "E2E verification failed (exit=$clientExit)"
                    Write-Host "  E2E: FAIL" -ForegroundColor Red
                }

                # ── 7. Stop server ────────────────────────────────────────
                Write-Host "  Stopping server..." -ForegroundColor Yellow
                Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
                Start-Sleep -Seconds 1

                # Show stderr for debugging
                if (Test-Path $serverErr) {
                    $errContent = Get-Content $serverErr -ErrorAction SilentlyContinue
                    if ($errContent) {
                        Write-Host "  Server stderr:" -ForegroundColor DarkGray
                        Write-Host ($errContent -join "`n") -ForegroundColor DarkGray
                    }
                }

            } catch {
                $result.Error = "Exception: $_"
                Write-Host "  EXCEPTION: $_" -ForegroundColor Red
                Get-Process dotnet -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
            }

            $results += $result
        }
    }
}

# ── Cleanup ─────────────────────────────────────────────────────────────────

if (-not $KeepScaffolds) {
    Write-Host "  Cleaning scaffold directories..." -ForegroundColor DarkGray
    foreach ($engine in $engines) {
        foreach ($transport in $transports) {
            foreach ($ser in $serializerList) {
                $dir = Join-Path $matrixDir "E2E_${engine}_${transport}_${serializerS}"
                if (Test-Path $dir) { Remove-Item -Recurse -Force $dir -ErrorAction SilentlyContinue }
            }
        }
    }
}

# ── Report ──────────────────────────────────────────────────────────────────

Write-Banner "RESULTS SUMMARY"
Write-Host ""

$passCount = ($results | Where-Object {
    $_.Scaffold -eq "PASS" -and $_.Build -eq "PASS" -and ($_.E2E -eq "PASS" -or $_.E2E -eq "SKIP")
}).Count
$failCount = $totalTests - $passCount

$results | Format-Table Engine, Transport, Serializer, Scaffold, Build, E2E -AutoSize

Write-Host ""
$color = if ($failCount -eq 0) { "Green" } else { "Red" }
Write-Host "Total: $totalTests | Passed: $passCount | Failed: $failCount" -ForegroundColor $color

if ($failCount -gt 0) {
    Write-Host ""
    Write-Host "Failures:" -ForegroundColor Red
    $results | Where-Object { $_.Scaffold -ne "PASS" -or $_.Build -ne "PASS" -or ($_.E2E -ne "PASS" -and $_.E2E -ne "SKIP") } | ForEach-Object {
        Write-Host "  $($_.Engine) / $($_.Transport) / $($_.Serializer): $($_.Error)" -ForegroundColor Red
    }
    exit 1
}

Write-Host ""
Write-Host "All tests passed!" -ForegroundColor Green
exit 0
