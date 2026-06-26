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

    [switch]$KeepArtifacts
)

$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$sampleRoot = Resolve-Path $scriptRoot
$repoRoot = Resolve-Path (Join-Path $sampleRoot "../..")
$serverProject = Join-Path $sampleRoot "Server/App/Server.App.csproj"
$hotfixProject = Join-Path $sampleRoot "Server/Hotfix/Server.Hotfix.csproj"
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
    <LakonaRpcGeneratedNamespace>Rpc.Generated</LakonaRpcGeneratedNamespace>
    <LakonaGameGenerateClient>true</LakonaGameGenerateClient>
    <LakonaGameClientRuntime>godot</LakonaGameClientRuntime>
    <LakonaGameClientPlatform>godot</LakonaGameClientPlatform>
    <LakonaGameClientGameVersion>chat-e2e</LakonaGameClientGameVersion>
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
using Lakona.Rpc.Client;
using Lakona.Rpc.Serializer.MemoryPack;
using Lakona.Rpc.Transport.WebSocket;
using Shared.Contracts.Chat;

static async Task<T> WaitAsync<T>(Task<T> task, TimeSpan timeout, string label)
{
    var completed = await Task.WhenAny(task, Task.Delay(timeout));
    if (!ReferenceEquals(completed, task))
    {
        throw new TimeoutException($"Timed out waiting for {label}.");
    }

    return await task;
}

var endpoint = args.Length > 0 ? args[0] : "ws://127.0.0.1:20000/ws";
var playerName = args.Length > 1 ? args[1] : "E2E-GodotChat";
var messageText = args.Length > 2 ? args[2] : "E2E-GodotChat message";
var timeout = TimeSpan.FromSeconds(15);

Console.WriteLine("[E2E] Starting client harness.");
Console.WriteLine("[E2E] Endpoint: {0}", endpoint);

await using var loginClient = new LoginClient(new RpcClientOptions(
    new WsTransport(endpoint),
    new MemoryPackRpcSerializer()));

var pushedMessage = new TaskCompletionSource<ChatMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
loginClient.OnMessageReceived += message =>
{
    Console.WriteLine("[E2E] Push received: {0}: {1}", message.SenderName, message.Text);
    if (message.SenderName == playerName && message.Text == messageText)
    {
        pushedMessage.TrySetResult(message);
    }
};

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

var chatClient = new ChatClient(loginClient);
await chatClient.BindAsync(loginReply);
Console.WriteLine("[E2E] BindAsync completed.");

await chatClient.SendAsync(messageText);
Console.WriteLine("[E2E] SendAsync completed.");

var received = await WaitAsync(pushedMessage.Task, timeout, "chat push");
if (received.Text != messageText || received.SenderName != playerName)
{
    throw new InvalidOperationException("Received chat push did not match the sent message.");
}

Console.WriteLine("[E2E] SUCCESS: Login response, chat send, and chat push verified.");
'@

    Set-Content -LiteralPath (Join-Path $harnessDir "Program.cs") -Value $programContent -Encoding UTF8
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
    $clientOutput = & dotnet run --project $harnessProject --no-build -- $endpoint $PlayerName $MessageText 2>&1
    $clientExitCode = $LASTEXITCODE
    $clientOutput | Set-Content -LiteralPath $clientOut -Encoding UTF8
    Write-Host ($clientOutput -join [Environment]::NewLine)

    if ($clientExitCode -ne 0) {
        throw "Client harness failed (exit code $clientExitCode). See $clientOut"
    }

    Write-Step "Game.Godot.Chat E2E passed"
}
finally {
    if ($serverProcess -and -not $serverProcess.HasExited) {
        Write-Host "Stopping server process $($serverProcess.Id)..." -ForegroundColor Yellow
        Stop-Process -Id $serverProcess.Id -Force -ErrorAction SilentlyContinue
        $serverProcess.WaitForExit(5000) | Out-Null
    }

    if ($null -eq $previousPort) { Remove-Item Env:Lakona__Endpoints__0__Port -ErrorAction SilentlyContinue } else { $env:Lakona__Endpoints__0__Port = $previousPort }
    if ($null -eq $previousHost) { Remove-Item Env:Lakona__Endpoints__0__Host -ErrorAction SilentlyContinue } else { $env:Lakona__Endpoints__0__Host = $previousHost }
    if ($null -eq $previousPath) { Remove-Item Env:Lakona__Endpoints__0__Path -ErrorAction SilentlyContinue } else { $env:Lakona__Endpoints__0__Path = $previousPath }
    if ($null -eq $previousMsBuildServer) { Remove-Item Env:DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER -ErrorAction SilentlyContinue } else { $env:DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER = $previousMsBuildServer }

    if (-not $KeepArtifacts) {
        Remove-Item -LiteralPath $harnessDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}
