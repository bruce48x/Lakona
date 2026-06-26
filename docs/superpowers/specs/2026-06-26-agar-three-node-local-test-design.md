# Agar Three-Node Local Test Design

## Goal

Provide a one-command local acceptance test for
`samples/Game.Unity.Agar` that starts the real three-node distributed server
topology and verifies it through the existing Unity client.

The test must prove that the current sample can run the intended local Agar
distributed flow:

1. `data-1`, `gateway-1`, and `battle-1` start as independent server
   processes under Docker Compose.
2. The Unity client connects to `gateway-1` over WebSocket.
3. The client performs guest login.
4. The client starts matchmaking.
5. Matchmaking returns a KCP realtime endpoint for `battle-1`.
6. The existing Unity client connects to `battle-1` over KCP.
7. The client attaches to the realtime room and receives world-state pushes.

This is a local developer test, not a cloud CI gate.

## Context

`docs/cluster.md` defines `samples/Game.Unity.Agar` as the first three-node
cluster acceptance sample. The sample already contains:

- `samples/Game.Unity.Agar/docker-compose.yml`
- `samples/Game.Unity.Agar/Server/App/appsettings.data-1.json`
- `samples/Game.Unity.Agar/Server/App/appsettings.gateway-1.json`
- `samples/Game.Unity.Agar/Server/App/appsettings.battle-1.json`
- a real Unity client under `samples/Game.Unity.Agar/Client`

Existing repository tests cover configuration shape, Docker build shape,
business logic, and local cluster primitives. They do not start the full
three-node Agar topology and drive the Unity client end to end.

## Design Summary

Add a local PowerShell script plus a Unity PlayMode smoke test.

The PowerShell script owns process orchestration:

- start and stop Docker Compose
- manage isolated test volumes by default
- pass host-facing advertised endpoint overrides into the server containers
- run Unity batchmode PlayMode tests
- collect logs and test results

The Unity PlayMode test owns gameplay acceptance:

- load `Assets/Scenes/Gameplay.unity`
- drive the existing `DotArenaGame` UI entry points
- wait for observable client states
- assert login, matchmaking, KCP realtime attach, and world-state receipt

The design intentionally uses the existing Unity client instead of a new
headless console client. `Game.Unity.Agar/Client` contains substantial
business flow, callback, reliable-push, and realtime connection behavior. A
separate console client would duplicate that logic and could pass while the
real sample client is broken.

## Script

Create `scripts/game/ci/test-agar-three-node.ps1`.

Despite living under `scripts/game/ci`, this script is a local test entry
point. It should not be added to default GitHub Actions or the default
solution test run.

Suggested parameters:

- `-UnityPath <path>`: optional path to the Unity executable.
- `-ProjectName <name>`: optional Docker Compose project name. Default:
  `lakona-agar-three-node-test`.
- `-TimeoutSeconds <seconds>`: overall wait timeout for readiness and Unity
  test execution. Default: 600.
- `-KeepEnvironment`: leave containers and volumes after the run.
- `-ReuseEnvironment`: do not clean existing containers or volumes before
  starting.
- `-SkipBuild`: pass through to Docker Compose when the caller wants to reuse
  existing images.

Tool discovery:

1. Require PowerShell 7 for script execution.
2. Verify `docker compose version` succeeds.
3. Resolve Unity from `-UnityPath`, then `UNITY_PATH`, then common Unity Hub
   install locations.
4. Fail with actionable text if Unity cannot be found.

Default environment behavior:

1. Use an isolated Compose project name.
2. Unless `-ReuseEnvironment` is set, run `docker compose down --volumes`
   for that project before startup.
3. Start the sample compose file with build enabled by default.
4. Unless `-KeepEnvironment` is set, shut the project down with volumes after
   the test finishes.

Host-facing endpoint overrides:

The server containers must keep cluster endpoint and seed configuration on the
Compose network. Only client-advertised endpoint hosts should be overridden so
the Unity client running on the host can reach them:

- gateway endpoint advertised host: `127.0.0.1`
- battle endpoint advertised host: `127.0.0.1`

The script should pass these as environment overrides to the relevant services:

- `Lakona__Endpoints__0__AdvertisedHost=127.0.0.1` for `gateway-1`
- `Lakona__Endpoints__0__AdvertisedHost=127.0.0.1` for `battle-1`

Readiness checks:

- Postgres and Redis health checks report healthy.
- `data-1`, `gateway-1`, and `battle-1` containers are running.
- TCP port `20000` accepts a connection from the host.
- KCP/UDP port `20001` is verified by the PlayMode test, not by a raw port
  probe.

Output artifacts:

- `.tmp/agar-three-node/TestResults.xml`
- `.tmp/agar-three-node/unity-editor.log`
- `.tmp/agar-three-node/docker-compose.log`
- `.tmp/agar-three-node/docker-compose.ps.json` when machine-readable compose
  status is useful

The script should print a compact summary and exit non-zero on any failure.

## Unity PlayMode Test

Create a PlayMode test assembly under:

```txt
samples/Game.Unity.Agar/Client/Assets/Tests/PlayMode/
```

Files:

- `SampleClient.Gameplay.PlayModeTests.asmdef`
- `DotArenaThreeNodePlayModeTests.cs`

The asmdef should reference the existing client assemblies:

- `SampleClient.Gameplay`
- `SampleClient.Rpc`
- `Shared`
- Unity Test Framework

The PlayMode test should use `[UnityTest]` and `IEnumerator`, following the
repository rule for Unity tests.

Test flow:

1. Load `Assets/Scenes/Gameplay.unity`.
2. Find the scene's `DotArenaGame`.
3. Apply endpoint override from command-line arguments, defaulting to
   `127.0.0.1:20000/ws`.
4. Call `OnUiMultiplayerSelected()`.
5. Call `OnUiGuestLoginRequested()`.
6. Wait until the test snapshot reports the multiplayer lobby state.
7. Start matchmaking through a test helper that uses the same existing client
   flow as the lobby primary action.
8. Wait until the test snapshot includes a KCP realtime endpoint.
9. Wait until realtime is connected.
10. Wait until the test snapshot reports `InMatch`.
11. Wait until `LastWorldTick >= 0` and `ViewCount > 0`.

The test must not construct RPC transports directly and must not duplicate
Agar business protocol logic. It should drive `DotArenaGame` and read an
observable snapshot.

## Test Observation API

Add `samples/Game.Unity.Agar/Client/Assets/Scripts/Gameplay/DotArenaGame.Testing.cs`.

Guard the test-only surface with Unity's test compilation symbol:

```csharp
#if UNITY_INCLUDE_TESTS
// test-only API
#endif
```

Expose a small immutable snapshot with plain values needed by PlayMode tests:

- flow state
- entry menu state
- session mode
- status
- local player id
- whether control is connected
- whether realtime is connected
- whether the client is connecting
- last world tick
- view count
- last realtime connection transport
- last realtime connection host
- last realtime connection port
- last realtime connection room id
- last realtime connection match id

Expose small test helpers:

- `ApplyEndpointForTest(string host, int port, string path)`
- `RequestMultiplayerMatchmakingForTest()`

`RequestMultiplayerMatchmakingForTest()` should call the same production path
as the lobby primary action. It must not bypass session, matchmaking, callback,
or realtime attach behavior.

This API is not a production API and should not be used by game code.

## Failure Model

Failures should name the failed phase.

Examples:

- `docker compose is not available`
- `Unity executable was not found`
- `Postgres did not become healthy`
- `data-1 exited before readiness`
- `gateway port 20000 was not reachable from the host`
- `Unity compilation failed`
- `DotArenaGame was not found in Gameplay.unity`
- `guest login did not reach multiplayer lobby`
- `matchmaking did not provide a KCP realtime endpoint`
- `KCP realtime connection did not attach`
- `world state was not received`

On failure, the script should print the tail of Unity and Docker logs and
point to the saved artifact directory.

## Documentation

Update `samples/Game.Unity.Agar/README.md` with a short local test section:

```powershell
pwsh -NoProfile -File scripts/game/ci/test-agar-three-node.ps1
```

Document:

- it is local-only
- it requires Docker and Unity
- it starts the real three-node topology
- `-KeepEnvironment` keeps containers and logs for debugging

Add a short cross-reference from `docs/cluster.md` near the Agar acceptance
section. Keep the detailed usage in the sample README.

## Non-Goals

- No cloud CI integration in the first version.
- No replacement console client for Agar gameplay smoke.
- No default `dotnet test Lakona.slnx` integration.
- No room migration testing.
- No battle-node failure or recovery testing.
- No multi-battle-node load balancing test.
- No production API for the test snapshot.

## Success Criteria

The design is implemented when a developer can run one local command from the
repository root and receive a pass/fail result that covers the real
`Game.Unity.Agar` three-node server topology and the existing Unity client
multiplayer flow through world-state receipt.
