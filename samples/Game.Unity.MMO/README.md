# Game.Unity.MMO

`Game.Unity.MMO` is a minimal MMORPG-style sample for **server-authoritative state synchronization**. It complements `Game.Unity.Agar`: Agar demonstrates client frame-sync simulation, while this project keeps movement, combat, death, respawn, monsters, and AOI on the server.

## Network shape

The client owns exactly **one WebSocket connection** to the `world` RPC service on `127.0.0.1:20100/ws`.

- client → server: enter/leave and sequenced movement or attack Commands;
- server → client: authoritative AOI-filtered World Snapshots at 10 Hz;
- client-only work: input collection, interpolation, camera, and presentation;
- server-only work: position integration, command validation, targeting, damage, death, respawn, monster AI, and interest management.

There is no KCP side channel, matchmaking connection, client battle settlement, or client-authored state.

## Run

1. Build Hotfix once so the stable host can load it:

   ```powershell
   dotnet build samples/Game.Unity.MMO/Server/Hotfix/Server.Hotfix.csproj
   ```

2. Start the server:

   ```powershell
   dotnet run --project samples/Game.Unity.MMO/Server/App/Server.App.csproj
   ```

3. Open `samples/Game.Unity.MMO/Client` with Unity 2022.3 LTS. Let NuGetForUnity restore the listed packages, then open `Assets/Scenes/World.unity`.
4. Enter Play Mode, type a character name, and select **Enter World**.

Use WASD or arrow keys to send movement intent. Hold Space to attack the nearest visible monster. Open a second Unity Editor or standalone build with another name to observe the same authoritative Zone.

## Architecture

`Server/App` contains stable host configuration and the `ZoneActor` state shell. `Server/Hotfix` owns all mutable game rules and the fixed-rate timer. `Shared` contains only RPC DTOs and constants required to interpret snapshots; it contains no battle simulation.

The first vertical slice deliberately uses one `greenfield` Zone and in-memory Character state. Production extensions should add account/Character persistence and explicit cross-Zone transfer without moving live Zone state into RPC services.

See [CONTEXT.md](CONTEXT.md) for the sample's domain language.

## Validate

```powershell
dotnet build samples/Game.Unity.MMO/Server/Hotfix/Server.Hotfix.csproj
dotnet test samples/Game.Unity.MMO/tests/BusinessLogic.Tests/BusinessLogic.Tests.csproj
```

In Unity Test Runner, run the EditMode suite to verify that `World.unity` remains playable, is included in the build, and contains the MMO client, camera, and authored world preview.
