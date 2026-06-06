# Unity.Json.Websocket

Minimal RPC sample over WebSocket with JSON serialization.

## Structure

- `Server`: .NET 10 WebSocket server
- `Client`: Unity 2022 LTS client

## Quick Start

Build or regenerate the sample from the repository root:

```powershell
pwsh -NoProfile -File .\scripts\sample.ps1 -Sample Unity.Json.Websocket
```

Run the server:

```powershell
pwsh -NoProfile -File .\scripts\sample.ps1 -Sample Unity.Json.Websocket -Run
```

The sample server enables connection keepalive by default and also accepts:

```powershell
--keepalive
--keepalive-interval 00:00:15
--keepalive-timeout 00:00:45
```

Open `samples/Unity.Json.Websocket/Client`, load `Assets/Scenes/ConnectionTest.unity`, and press Play.

The Unity client opens multiple WebSocket connections to `ws://127.0.0.1:20000/ws`, logs in, then keeps calling `IncrStep()`. The server maintains one counter per connection and sends updates through `IPlayerNotifications.OnNotify(...)`.

The Unity client entry now uses `RpcClientOptions` plus the generated `RpcClient.Api` facade:

```csharp
var options = new RpcClientOptions(
    new WsTransport(_endpoint.GetWebSocketUrl()),
    new JsonRpcSerializer())
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
