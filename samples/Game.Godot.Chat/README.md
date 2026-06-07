# Game.Godot.Chat

`Game.Godot.Chat` is the single-endpoint Godot sample for Lakona.Game.

## What This Sample Demonstrates

- One WebSocket RPC endpoint for normal client/server traffic.
- Chat contracts in `Shared/Contracts/Chat`.
- RPC adapter code in `Server/Server/Chat/ChatServiceImpl.cs`.
- Actor-owned Chat state in `Server/Server/Chat/ChatRoomActor.cs`.
- Hotfix message filtering through `Server/Server/Chat/ChatRules.cs` and `Server/Hotfix/Chat/ChatRulesSystem.cs`.

## Comparison With Game.Unity.Agar

Use `Game.Godot.Chat` when you want the smallest single-endpoint project shape.
Use `samples/Game.Unity.Agar` when you want the richer multi-endpoint realtime game shape with WebSocket control plus KCP realtime.

This sample intentionally does not include matchmaking, KCP, realtime attach, arena simulation, WASD input, or world-state snapshots.
