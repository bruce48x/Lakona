# Unity.MemoryPack.Tcp

MemoryPack-based RPC sample over TCP.

## Structure

- `Server`: .NET 10 TCP server
- `Client`: Unity 2022 LTS client

## Quick Start

Build or regenerate the sample from the repository root:

```powershell
pwsh -NoProfile -File .\scripts\sample.ps1 -Sample Unity.MemoryPack.Tcp
```

Run the server:

```powershell
pwsh -NoProfile -File .\scripts\sample.ps1 -Sample Unity.MemoryPack.Tcp -Run
```

The sample server enables connection keepalive by default and also accepts:

```powershell
--keepalive
--keepalive-interval 00:00:15
--keepalive-timeout 00:00:45
```

Open `samples/Unity.MemoryPack.Tcp/Client`, load `Assets/Scenes/ConnectionTest.unity`, and press Play.

The Unity client opens multiple TCP connections. Each connection uses three independent services:

- `IPlayerService`
- `IInventoryService`
- `IQuestService`

Each service has its own notification contract. After the player login succeeds, the client keeps calling:

- `IPlayerService.IncrStep()`
- `IInventoryService.IncrRevision()`
- `IQuestService.IncrProgress()`

The server pushes updates back through:

- `IPlayerNotifications.OnPlayerNotify(...)`
- `IInventoryNotifications.OnInventoryNotify(...)`
- `IQuestNotifications.OnQuestNotify(...)`

The shared MemoryPack DTOs in `Packages/com.samples.contracts/ExampleDtos.cs` use `GenerateType.VersionTolerant` plus explicit `MemoryPackOrder(...)` numbering. This keeps payload evolution safer when newer and older client/server builds coexist, so adding optional fields later is less likely to break cross-version communication.

The Unity client entry now uses `RpcClientOptions` plus the generated `RpcClient.Api` facade:

```csharp
var options = new RpcClientOptions(
    new TcpTransport(_endpoint.Host, _endpoint.Port),
    new MemoryPackRpcSerializer())
{
    KeepAlive = new RpcKeepAliveOptions
    {
        Enabled = true,
        Interval = TimeSpan.FromSeconds(15),
        Timeout = TimeSpan.FromSeconds(45)
    }
};

await using var client = new RpcClient(options, callbacks);
await client.ConnectAsync();

var player = client.Api.Game.Player;
```
