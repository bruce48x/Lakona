#Requires -Version 5.1
<#
.SYNOPSIS
    Dedicated end-to-end test for samples/Game.Godot.Chat.

.DESCRIPTION
    Builds the sample server, starts it as a real process, builds a temporary
    console client harness from the sample's LoginClient.cs and ChatClient.cs,
    sends real WebSocket/MemoryPack RPC requests, verifies the login response,
    binds chat, sends a chat message, verifies the pushed chat callback, and
    stops the server.
#>

[CmdletBinding()]
param(
    [int]$Port = 20000,

    [int]$TimeoutSeconds = 30,

    [string]$PlayerName = "E2E-GodotChat",

    [string]$MessageText = "E2E-GodotChat message",

    [switch]$VerifyHotfixWatcher,

    [switch]$KeepArtifacts
)

$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$sampleRoot = Resolve-Path $scriptRoot
$repoRoot = Resolve-Path (Join-Path $sampleRoot "../..")
$serverProject = Join-Path $sampleRoot "Server/App/Server.App.csproj"
$hotfixProject = Join-Path $sampleRoot "Server/Hotfix/Server.Hotfix.csproj"
$chatServiceSource = Join-Path $sampleRoot "Server/Hotfix/Chat/ChatService.cs"
$artifactsRoot = Join-Path $sampleRoot "_artifacts/e2e"
$harnessDir = Join-Path $artifactsRoot "client-harness"
$serverOut = Join-Path $artifactsRoot "server.out.log"
$serverErr = Join-Path $artifactsRoot "server.err.log"
$clientOut = Join-Path $artifactsRoot "client.out.log"
$endpoint = "ws://127.0.0.1:$Port/ws"
$serverProcess = $null
$previousPort = $env:Lakona__Endpoints__0__Port
$previousHost = $env:Lakona__Endpoints__0__Host
$previousPath = $env:Lakona__Endpoints__0__Path
$previousMsBuildServer = $env:DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER
$chatServiceOriginalBytes = $null
$chatServiceSourceRestored = $false

function Write-Step {
    param([string]$Text)
    Write-Host ""
    Write-Host "==> $Text" -ForegroundColor Cyan
}

function Invoke-Checked {
    param(
        [string]$FilePath,
        [string[]]$Arguments,
        [string]$FailureMessage
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$FailureMessage (exit code $LASTEXITCODE)"
    }
}

function Wait-Port {
    param(
        [string]$HostName,
        [int]$HostPort,
        [int]$Seconds,
        [System.Diagnostics.Process]$Process
    )

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($Seconds)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        if ($Process.HasExited) {
            return $false
        }

        $client = [System.Net.Sockets.TcpClient]::new()
        try {
            $connect = $client.ConnectAsync($HostName, $HostPort)
            if ($connect.Wait(500) -and $client.Connected) {
                return $true
            }
        }
        catch {
        }
        finally {
            $client.Dispose()
        }

        Start-Sleep -Milliseconds 500
    }

    return $false
}

function Test-PortOpen {
    param(
        [string]$HostName,
        [int]$HostPort
    )

    $client = [System.Net.Sockets.TcpClient]::new()
    try {
        $connect = $client.ConnectAsync($HostName, $HostPort)
        return $connect.Wait(300) -and $client.Connected
    }
    catch {
        return $false
    }
    finally {
        $client.Dispose()
    }
}

function Assert-PortAvailable {
    param(
        [string]$HostName,
        [int]$HostPort
    )

    if (Test-PortOpen -HostName $HostName -HostPort $HostPort) {
        throw "Port $HostName`:$HostPort already accepts connections before the E2E server starts. Stop the existing process or rerun with -Port."
    }
}

function Wait-FileContains {
    param(
        [string]$Path,
        [string]$Text,
        [int]$Seconds,
        [System.Diagnostics.Process]$Process
    )

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($Seconds)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        if ($Process -and $Process.HasExited) {
            return $false
        }

        if (Test-Path -LiteralPath $Path) {
            try {
                $content = Get-Content -Raw -LiteralPath $Path
                if ($content.Contains($Text)) {
                    return $true
                }
            }
            catch {
            }
        }

        Start-Sleep -Milliseconds 500
    }

    return $false
}

function Get-FileTail {
    param(
        [string]$Path,
        [int]$LineCount = 80
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        return ""
    }

    return (Get-Content -LiteralPath $Path -Tail $LineCount) -join [Environment]::NewLine
}

function Restore-ChatServiceSource {
    if ($null -ne $script:chatServiceOriginalBytes) {
        [System.IO.File]::WriteAllBytes($script:chatServiceSource, $script:chatServiceOriginalBytes)
        $script:chatServiceOriginalBytes = $null
        $script:chatServiceSourceRestored = $true
    }
}

function Restore-HotfixOutput {
    if (-not $script:chatServiceSourceRestored) {
        return
    }

    try {
        Write-Step "Restore hotfix output after ChatService reset"
        Invoke-Checked -FilePath "dotnet" -Arguments @("build", $script:hotfixProject, "--nologo", "--no-restore", "--no-dependencies") -FailureMessage "Hotfix output restore build failed"
    }
    catch {
        Write-Warning $_
    }
}

function Set-ChatServiceWatcherLog {
    param([string]$Token)

    if (-not (Test-Path -LiteralPath $chatServiceSource)) {
        throw "Missing ChatService source file: $chatServiceSource"
    }

    $script:chatServiceOriginalBytes = [System.IO.File]::ReadAllBytes($chatServiceSource)
    $content = [System.IO.File]::ReadAllText($chatServiceSource)
    $oldLine = '            _logger.LogInformation("Sending {CharacterCount} characters", text.Length);'
    $newLine = "            _logger.LogInformation(`"LakonaHotfixWatcherE2E $Token {CharacterCount} characters`", text.Length);"
    if (-not $content.Contains($oldLine)) {
        Restore-ChatServiceSource
        throw "Could not find the expected SendAsync log line in $chatServiceSource"
    }

    [System.IO.File]::WriteAllText($chatServiceSource, $content.Replace($oldLine, $newLine))
}

function Write-HarnessProject {
    New-Item -ItemType Directory -Force -Path $harnessDir | Out-Null

    $sharedProject = Join-Path $sampleRoot "Shared/Shared.csproj"
    $loginClientSource = Join-Path $sampleRoot "Client/Scripts/Login/LoginClient.cs"
    $chatClientSource = Join-Path $sampleRoot "Client/Scripts/Chat/ChatClient.cs"
    $coreProject = Join-Path $repoRoot "src/Lakona.Rpc.Core/Lakona.Rpc.Core.csproj"
    $rpcClientProject = Join-Path $repoRoot "src/Lakona.Rpc.Client/Lakona.Rpc.Client.csproj"
    $webSocketProject = Join-Path $repoRoot "src/Lakona.Rpc.Transport.WebSocket/Lakona.Rpc.Transport.WebSocket.csproj"
    $memoryPackProject = Join-Path $repoRoot "src/Lakona.Rpc.Serializer.MemoryPack/Lakona.Rpc.Serializer.MemoryPack.csproj"
    $gameAbstractionsProject = Join-Path $repoRoot "src/Lakona.Game.Abstractions/Lakona.Game.Abstractions.csproj"
    $gameClientProject = Join-Path $repoRoot "src/Lakona.Game.Client/Lakona.Game.Client.csproj"
    $analyzerProject = Join-Path $repoRoot "src/Lakona.Rpc.Analyzers/Lakona.Rpc.Analyzers.csproj"

    $projectContent = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
    <LakonaRpcGenerateClient>true</LakonaRpcGenerateClient>
    <LakonaRpcGeneratedNamespace>Client.Generated</LakonaRpcGeneratedNamespace>
    <LakonaGameGenerateClient>true</LakonaGameGenerateClient>
  </PropertyGroup>

  <ItemGroup>
    <CompilerVisibleProperty Include="LakonaRpcGenerateClient" />
    <CompilerVisibleProperty Include="LakonaRpcGeneratedNamespace" />
    <CompilerVisibleProperty Include="LakonaGameGenerateClient" />
  </ItemGroup>

  <ItemGroup>
    <Compile Include="$loginClientSource" Link="Client/Login/LoginClient.cs" />
    <Compile Include="$chatClientSource" Link="Client/Chat/ChatClient.cs" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="$sharedProject" TargetFramework="net10.0">
      <SetTargetFramework>TargetFramework=net10.0</SetTargetFramework>
    </ProjectReference>
    <ProjectReference Include="$coreProject" />
    <ProjectReference Include="$rpcClientProject" />
    <ProjectReference Include="$webSocketProject" />
    <ProjectReference Include="$memoryPackProject" />
    <ProjectReference Include="$gameAbstractionsProject" />
    <ProjectReference Include="$gameClientProject" />
    <ProjectReference Include="$analyzerProject" ReferenceOutputAssembly="false" OutputItemType="Analyzer" PrivateAssets="all" />
  </ItemGroup>
</Project>
"@

    Set-Content -LiteralPath (Join-Path $harnessDir "Game.Godot.Chat.E2E.Client.csproj") -Value $projectContent -Encoding UTF8

    $programContent = @'
using Client.Chat;
using Client.Login;
using Lakona.Game.Client;
using Lakona.Game.Client.Sessions;
using Lakona.Rpc.Core;
using Lakona.Rpc.Serializer.MemoryPack;
using Lakona.Rpc.Transport.WebSocket;
using Shared.Contracts.Chat;
using System.Collections.Concurrent;

var endpoint = args.Length > 0 ? args[0] : "ws://127.0.0.1:20000/ws";
var playerName = args.Length > 1 ? args[1] : "E2E-GodotChat";
var messageText = args.Length > 2 ? args[2] : "E2E-GodotChat message";
var timeout = TimeSpan.FromSeconds(15);

Console.WriteLine("[E2E] Starting client harness.");
Console.WriteLine("[E2E] Endpoint: {0}", endpoint);

var gate = new TestTransportGate();
await using var loginClient = new LoginClient(new LakonaGameClientOptions(
    () => gate.Wrap(new WsTransport(endpoint)),
    new MemoryPackRpcSerializer()));

var pushedMessages = new ConcurrentQueue<ChatMessage>();
var pushCounts = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);
var pushAvailable = new SemaphoreSlim(0);
loginClient.OnMessageReceived += message =>
{
    Console.WriteLine("[E2E] Push received: {0}: {1}", message.SenderName, message.Text);
    pushedMessages.Enqueue(message);
    pushCounts.AddOrUpdate(message.SenderName + "\n" + message.Text, 1, static (_, count) => count + 1);
    pushAvailable.Release();
};

async Task<ChatMessage> WaitForPushAsync(string sender, string text, string label)
{
    var deadline = DateTimeOffset.UtcNow + timeout;
    while (DateTimeOffset.UtcNow < deadline)
    {
        while (pushedMessages.TryDequeue(out var message))
        {
            if (message.SenderName == sender && message.Text == text)
            {
                return message;
            }
        }

        var remaining = deadline - DateTimeOffset.UtcNow;
        await pushAvailable.WaitAsync(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero);
    }

    throw new TimeoutException($"Timed out waiting for {label}.");
}

async Task WaitForPhaseAsync(ClientSessionPhase phase, string label)
{
    var deadline = DateTimeOffset.UtcNow + timeout;
    while (DateTimeOffset.UtcNow < deadline)
    {
        if (loginClient.GameClient.Snapshot.Phase == phase)
        {
            return;
        }

        await Task.Delay(20);
    }

    throw new TimeoutException($"Timed out waiting for {label}; current phase is {loginClient.GameClient.Snapshot.Phase}.");
}

await loginClient.ConnectAsync();
Console.WriteLine("[E2E] Connected.");

var loginReply = await loginClient.LoginAsync(playerName);
Console.WriteLine("[E2E] LoginAsync returned {0} member(s) and {1} recent message(s).",
    loginReply.Members.Count,
    loginReply.RecentMessages.Count);

if (!loginReply.Members.Any(member => member.Name == playerName))
{
    throw new InvalidOperationException("LoginAsync reply did not include the E2E player.");
}

var originalSessionId = loginClient.GameClient.Snapshot.SessionId;
var originalSessionGeneration = loginClient.GameClient.Snapshot.SessionGeneration;
if (loginClient.GameClient.Snapshot.Phase != ClientSessionPhase.Active ||
    string.IsNullOrWhiteSpace(originalSessionId) ||
    originalSessionGeneration <= 0)
{
    throw new InvalidOperationException("LoginAsync completed before the framework Session establishment barrier.");
}

var chatClient = new ChatClient(loginClient);
await chatClient.BindAsync(loginReply);
Console.WriteLine("[E2E] BindAsync completed.");

await chatClient.SendAsync(messageText);
Console.WriteLine("[E2E] SendAsync completed.");

var received = await WaitForPushAsync(playerName, messageText, "chat push");
if (received.Text != messageText || received.SenderName != playerName)
{
    throw new InvalidOperationException("Received chat push did not match the sent message.");
}

var peerName = playerName + "-peer";
await using var peer = new LoginClient(new LakonaGameClientOptions(
    () => new WsTransport(endpoint),
    new MemoryPackRpcSerializer()));
await peer.ConnectAsync();
var peerReply = await peer.LoginAsync(peerName);
var peerChat = new ChatClient(peer);
await peerChat.BindAsync(peerReply);

await gate.SetOpenAsync(false);
await WaitForPhaseAsync(ClientSessionPhase.Reconnecting, "client to enter reconnecting");

var offlineOne = messageText + " offline-1";
var offlineTwo = messageText + " offline-2";
await peerChat.SendAsync(offlineOne);
await peerChat.SendAsync(offlineTwo);

await gate.SetOpenAsync(true);
await WaitForPhaseAsync(ClientSessionPhase.Active, "client recovery");
if (loginClient.GameClient.Snapshot.SessionId != originalSessionId ||
    loginClient.GameClient.Snapshot.SessionGeneration != originalSessionGeneration)
{
    throw new InvalidOperationException("Framework recovery did not preserve the original Game Session generation.");
}

var replayedOne = await WaitForPushAsync(peerName, offlineOne, "first offline replay");
var replayedTwo = await WaitForPushAsync(peerName, offlineTwo, "second offline replay");
if (replayedOne.Text != offlineOne || replayedTwo.Text != offlineTwo)
{
    throw new InvalidOperationException("Reliable replay was not delivered in order.");
}

var postRecovery = messageText + " after-recovery";
await chatClient.SendAsync(postRecovery);
await WaitForPushAsync(playerName, postRecovery, "post-recovery push through held proxy");

if (pushCounts.GetValueOrDefault(peerName + "\n" + offlineOne) != 1 ||
    pushCounts.GetValueOrDefault(peerName + "\n" + offlineTwo) != 1)
{
    throw new InvalidOperationException("Reliable replay delivered a duplicate offline message.");
}

Console.WriteLine("[E2E] SUCCESS: login, stable-session recovery, ordered reliable replay, and held proxy reuse verified.");

sealed class TestTransportGate
{
    private readonly object _gate = new();
    private readonly HashSet<GatedTransport> _active = new();
    private bool _open = true;

    public ITransport Wrap(ITransport inner) => new GatedTransport(this, inner);

    public async Task SetOpenAsync(bool open)
    {
        GatedTransport[] active;
        lock (_gate)
        {
            _open = open;
            active = open ? Array.Empty<GatedTransport>() : _active.ToArray();
        }

        foreach (var transport in active)
        {
            await transport.DisposeAsync();
        }
    }

    private bool TryActivate(GatedTransport transport)
    {
        lock (_gate)
        {
            if (!_open)
            {
                return false;
            }

            _active.Add(transport);
            return true;
        }
    }

    private void Remove(GatedTransport transport)
    {
        lock (_gate)
        {
            _active.Remove(transport);
        }
    }

    private sealed class GatedTransport : ITransport
    {
        private readonly TestTransportGate _owner;
        private readonly ITransport _inner;
        private int _disposed;

        public GatedTransport(TestTransportGate owner, ITransport inner)
        {
            _owner = owner;
            _inner = inner;
        }

        public bool IsConnected => Volatile.Read(ref _disposed) == 0 && _inner.IsConnected;

        public async ValueTask ConnectAsync(CancellationToken cancellationToken = default)
        {
            await _inner.ConnectAsync(cancellationToken);
            if (!_owner.TryActivate(this))
            {
                await DisposeAsync();
                throw new IOException("The E2E transport gate is closed.");
            }
        }

        public ValueTask SendFrameAsync(ReadOnlyMemory<byte> frame, CancellationToken cancellationToken = default) =>
            _inner.SendFrameAsync(frame, cancellationToken);

        public ValueTask<TransportFrame> ReceiveFrameAsync(CancellationToken cancellationToken = default) =>
            _inner.ReceiveFrameAsync(cancellationToken);

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            _owner.Remove(this);
            await _inner.DisposeAsync();
        }
    }
}
'@

    Set-Content -LiteralPath (Join-Path $harnessDir "Program.cs") -Value $programContent -Encoding UTF8
}

function Invoke-ClientFlow {
    param(
        [string]$Label,
        [string]$Text
    )

    $clientOutput = & dotnet run --project $harnessProject --no-build -- $endpoint $PlayerName $Text 2>&1
    $clientExitCode = $LASTEXITCODE
    Add-Content -LiteralPath $clientOut -Value ""
    Add-Content -LiteralPath $clientOut -Value "== $Label =="
    $clientOutput | Add-Content -LiteralPath $clientOut -Encoding UTF8
    Write-Host ($clientOutput -join [Environment]::NewLine)

    if ($clientExitCode -ne 0) {
        throw "Client harness failed during '$Label' (exit code $clientExitCode). See $clientOut"
    }
}

function Invoke-HotfixWatcherVerification {
    Write-Step "Verify hotfix watcher reloads changed ChatService"

    if (-not (Wait-FileContains -Path $serverOut -Text "Sending" -Seconds 5 -Process $serverProcess)) {
        throw "The baseline SendAsync log was not observed before hotfix watcher verification.`nServer log tail:`n$(Get-FileTail -Path $serverOut)"
    }

    $token = "LakonaHotfixWatcherE2E-$([Guid]::NewGuid().ToString("N"))"
    Set-ChatServiceWatcherLog -Token $token

    Write-Step "Rebuild hotfix after changing ChatService"
    Invoke-Checked -FilePath "dotnet" -Arguments @("build", $hotfixProject, "--nologo", "--no-restore", "--no-dependencies") -FailureMessage "Hotfix watcher rebuild failed"

    $reloadSignal = Join-Path (Split-Path -Parent $serverProject) "bin/Debug/net10.0/hotfix/reload.signal"
    if (-not (Test-Path -LiteralPath $reloadSignal)) {
        throw "Hotfix rebuild did not write reload.signal: $reloadSignal"
    }

    for ($attempt = 1; $attempt -le 10; $attempt++) {
        Invoke-ClientFlow -Label "hotfix watcher attempt $attempt" -Text "$MessageText hotfix watcher $attempt"
        if (Wait-FileContains -Path $serverOut -Text $token -Seconds 2 -Process $serverProcess) {
            Write-Host "Hotfix watcher reload observed with token $token" -ForegroundColor Green
            return
        }

        Start-Sleep -Seconds 1
    }

    throw "Hotfix watcher token '$token' was not observed after rebuilding hotfix and sending messages.`nServer log tail:`n$(Get-FileTail -Path $serverOut)"
}

try {
    Set-Location $repoRoot
    New-Item -ItemType Directory -Force -Path $artifactsRoot | Out-Null
    Remove-Item -LiteralPath $serverOut, $serverErr, $clientOut -Force -ErrorAction SilentlyContinue

    $env:DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER = "1"

    Write-Step "Build Game.Godot.Chat server"
    Invoke-Checked -FilePath "dotnet" -Arguments @("build", $serverProject, "--nologo") -FailureMessage "Server build failed"

    Write-Step "Build Game.Godot.Chat hotfix"
    Invoke-Checked -FilePath "dotnet" -Arguments @("build", $hotfixProject, "--nologo") -FailureMessage "Hotfix build failed"

    Write-Step "Generate dedicated E2E client harness"
    Write-HarnessProject
    $harnessProject = Join-Path $harnessDir "Game.Godot.Chat.E2E.Client.csproj"

    Write-Step "Build E2E client harness"
    Invoke-Checked -FilePath "dotnet" -Arguments @("build", $harnessProject, "--nologo") -FailureMessage "Client harness build failed"

    Write-Step "Start server process"
    $env:Lakona__Endpoints__0__Host = "127.0.0.1"
    $env:Lakona__Endpoints__0__Port = [string]$Port
    $env:Lakona__Endpoints__0__Path = "/ws"
    Assert-PortAvailable -HostName "127.0.0.1" -HostPort $Port
    $serverProcess = Start-Process -FilePath "dotnet" `
        -ArgumentList @("run", "--project", $serverProject, "--no-build") `
        -PassThru `
        -NoNewWindow `
        -RedirectStandardOutput $serverOut `
        -RedirectStandardError $serverErr

    if (-not (Wait-Port -HostName "127.0.0.1" -HostPort $Port -Seconds $TimeoutSeconds -Process $serverProcess)) {
        $stderr = if (Test-Path -LiteralPath $serverErr) { Get-Content -Raw -LiteralPath $serverErr } else { "" }
        $stdout = if (Test-Path -LiteralPath $serverOut) { Get-Content -Raw -LiteralPath $serverOut } else { "" }
        throw "Server did not listen on 127.0.0.1:$Port within $TimeoutSeconds seconds.`nSTDOUT:`n$stdout`nSTDERR:`n$stderr"
    }

    Write-Host "Server is listening at $endpoint" -ForegroundColor Green

    Write-Step "Run real client RPC flow"
    Invoke-ClientFlow -Label "baseline" -Text $MessageText

    if ($VerifyHotfixWatcher) {
        Invoke-HotfixWatcherVerification
    }

    Write-Step "Game.Godot.Chat E2E passed"
}
finally {
    Restore-ChatServiceSource

    if ($serverProcess -and -not $serverProcess.HasExited) {
        Write-Host "Stopping server process $($serverProcess.Id)..." -ForegroundColor Yellow
        Stop-Process -Id $serverProcess.Id -Force -ErrorAction SilentlyContinue
        $serverProcess.WaitForExit(5000) | Out-Null
    }

    Restore-HotfixOutput

    if ($null -eq $previousPort) { Remove-Item Env:Lakona__Endpoints__0__Port -ErrorAction SilentlyContinue } else { $env:Lakona__Endpoints__0__Port = $previousPort }
    if ($null -eq $previousHost) { Remove-Item Env:Lakona__Endpoints__0__Host -ErrorAction SilentlyContinue } else { $env:Lakona__Endpoints__0__Host = $previousHost }
    if ($null -eq $previousPath) { Remove-Item Env:Lakona__Endpoints__0__Path -ErrorAction SilentlyContinue } else { $env:Lakona__Endpoints__0__Path = $previousPath }
    if ($null -eq $previousMsBuildServer) { Remove-Item Env:DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER -ErrorAction SilentlyContinue } else { $env:DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER = $previousMsBuildServer }

    if (-not $KeepArtifacts) {
        Remove-Item -LiteralPath $harnessDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}
