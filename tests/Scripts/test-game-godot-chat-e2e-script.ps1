#Requires -Version 5.1

$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptRoot "../..")
$target = Join-Path $repoRoot "samples/Game.Godot.Chat/test-game-godot-chat-e2e.ps1"

if (-not (Test-Path -LiteralPath $target)) {
    throw "Missing dedicated Game.Godot.Chat E2E script: $target"
}

$content = Get-Content -Raw -LiteralPath $target
$requiredFragments = @(
    "samples/Game.Godot.Chat",
    "Server/App/Server.App.csproj",
    "Server/Hotfix/Server.Hotfix.csproj",
    "LoginClient.cs",
    "ChatClient.cs",
    "Start-Process",
    "dotnet run",
    "LoginAsync",
    "BindAsync",
    "SendAsync",
    "OnMessageReceived",
    "VerifyHotfixWatcher",
    "ChatService.cs",
    "reload.signal",
    "LakonaHotfixWatcherE2E",
    "Assert-PortAvailable",
    "Wait-FileContains",
    "Restore-ChatServiceSource",
    "Restore-HotfixOutput",
    "E2E-GodotChat"
)

foreach ($fragment in $requiredFragments) {
    if ($content -notlike "*$fragment*") {
        throw "Expected '$fragment' in $target"
    }
}

Write-Host "Game.Godot.Chat E2E script contract: PASS"
