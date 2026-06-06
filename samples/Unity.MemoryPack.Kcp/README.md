# Unity.MemoryPack.Kcp

MemoryPack-based RPC sample over KCP.

## Structure

- `Server`: .NET 10 KCP server
- `Client`: Unity 2022 LTS client

## Quick Start

Build or regenerate the sample from the repository root:

```powershell
pwsh -NoProfile -File .\scripts\sample.ps1 -Sample Unity.MemoryPack.Kcp
```

Run the server:

```powershell
pwsh -NoProfile -File .\scripts\sample.ps1 -Sample Unity.MemoryPack.Kcp -Run
```

Open `samples/Unity.MemoryPack.Kcp/Client`, load `Assets/Scenes/ConnectionTest.unity`, and press Play.

The Unity client opens one KCP connection, logs in, then calls `IncrStep()` at a fixed interval. The server creates one `PlayerService` per `RpcSession`, so each client connection keeps its own counter.

The shared MemoryPack DTOs in `Packages/com.samples.contracts/ExampleDtos.cs` use `GenerateType.VersionTolerant` plus explicit `MemoryPackOrder(...)` numbering. This is intentional for compatibility when newer and older builds overlap, so DTO fields can evolve with a lower risk of breaking existing clients or servers.

The client entry is intentionally minimal:

```csharp
var options = new RpcClientOptions(
    new KcpTransport(_endpoint.Host, _endpoint.Port),
    new MemoryPackRpcSerializer());

await using var client = new RpcClient(options, callbacks);
await client.ConnectAsync();
var player = client.Api.Game.Player;
```
