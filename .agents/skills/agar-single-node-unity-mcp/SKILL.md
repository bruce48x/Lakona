---
name: agar-single-node-unity-mcp
description: Run Game.Unity.Agar single-node local validation with Server.App started by dotnet run and the Unity client driven through MCP for Unity. Use when the user asks to verify Agar login, guest login, matchmaking, five-second AI-fill matchmaking, KCP realtime attach, multiplayer battle smoke behavior, settlement-adjacent checks, or any single-node Game.Unity.Agar client/server regression with Unity Editor already open.
metadata:
  internal: true
---

# Agar Single Node Unity MCP

## Preconditions

- Unity Editor must already be open on `samples/Game.Unity.Agar/Client`.
- MCP for Unity must already be connected and listening on `127.0.0.1:8180`.
- If either Unity Editor or MCP for Unity is missing, run the prep script and stop when it reports the preflight error. Do not launch Unity Editor from this skill; ask the user to start it manually.
- Use PowerShell 7 (`pwsh`) from the repository root.

## Start Single-Node Server

Run the prep script before every Unity MCP test:

```powershell
pwsh -NoProfile -File scripts/game/local/test-agar-single-node-unity-mcp.ps1 -Scenario Matchmaking -StopExisting
```

What the script does:

- Fails early if Unity Editor or MCP for Unity is not running.
- Builds `samples/Game.Unity.Agar/Server/App/Server.App.csproj`.
- Builds `samples/Game.Unity.Agar/Server/Hotfix/Server.Hotfix.csproj`.
- Starts the Agar server with `dotnet run --project samples/Game.Unity.Agar/Server/App/Server.App.csproj --configuration Debug --no-build`.
- Clears cluster/multi-node environment overrides so the server runs in single-node local mode.
- Waits for the WebSocket control endpoint and KCP endpoint to become ready.
- Writes artifacts under `.tmp/agar-single-node-unity-mcp`.

The script defaults to `--no-restore` builds. If package assets are stale, rerun with `-Restore`.

## Unity MCP Test Step

After the script reports `Ready`, drive the client through MCP for Unity:

1. Refresh scripts if Unity code changed: `refresh_unity` with `scope="scripts"`, `mode="if_dirty"`, `compile="request"`, and `wait_for_ready=true`.
2. Clear the Unity console.
3. Run the PlayMode test `SampleClient.Gameplay.Tests.DotArenaThreeNodePlayModeTests.UnityClientCompletesThreeNodeMultiplayerSmoke`.
4. Poll the test job until completion.
5. On failure, inspect the Unity console, the PlayMode failure snapshot, `.tmp/agar-single-node-unity-mcp/server.out.log`, and `.tmp/agar-single-node-unity-mcp/server.err.log`.

Use MCP for Unity tools/resources for the Unity side. Do not use Unity batchmode for this skill.

## Scenario Mapping

- `Login`: Use `-Scenario Login`. The current PlayMode smoke proceeds past login, so a later matchmaking or battle failure can still prove login succeeded if the snapshot shows the lobby stage, `control=True`, and a player id.
- `Matchmaking`: Use `-Scenario Matchmaking`. The PlayMode smoke validates guest login, `StartMatchmaking`, queued state, the five-second AI-fill path, and receipt of a matched KCP endpoint.
- `Battle`: Use `-Scenario Battle` or `-Scenario Smoke`. The PlayMode smoke validates KCP realtime attach, entering the match, and receiving world state.
- `Settlement`: There is no dedicated Unity MCP settlement PlayMode test yet. Use this skill to validate login/match/battle client-server flow, then run targeted business logic tests when settlement code changed:

```powershell
dotnet test samples/Game.Unity.Agar/tests/BusinessLogic.Tests/BusinessLogic.Tests.csproj --no-restore --filter "FullyQualifiedName~ArenaSimulationRulesTests|FullyQualifiedName~PlayerSessionActorStateTests|FullyQualifiedName~AgarSessionLifecycleTests"
```

## Cleanup

Always stop the server started by this skill before finishing:

```powershell
pwsh -NoProfile -File scripts/game/local/test-agar-single-node-unity-mcp.ps1 -Stop
```

## Failure Triage

- Preflight failure: Unity Editor or MCP for Unity is not running; ask the user to start them manually.
- Build failure: inspect build output; rerun with `-Restore` only if restore is needed.
- Server readiness failure: inspect `.tmp/agar-single-node-unity-mcp/server.out.log` and `.tmp/agar-single-node-unity-mcp/server.err.log`.
- Unity failure: inspect the PlayMode failure snapshot first, then Unity console and server logs.
