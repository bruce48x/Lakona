# Lakona.Game.Client

`Lakona.Game.Client` contains reusable engine-neutral Game client primitives.
Generated game projects use a project-specific `Client.Generated.LakonaGameClient`
as the public entry point. The fixed package type is `LakonaGameClientCore`,
which owns framework handshake state, reliable push state, heartbeat state, and
session snapshots.

The library does not depend on Unity, Godot, or any transport package. Game
clients remain responsible for choosing their transport, dispatching callbacks
onto the engine main thread, and applying business-specific payloads.

## Generated Client Entry Point

Generated game projects should use their generated wrapper as the single
connection entry point:

```csharp
using Client.Generated;
using Lakona.Game.Client;

var options = new LakonaGameClientOptions(transport, serializer)
{
};

await using var gameClient = new LakonaGameClient(options, callbackReceiver);
await gameClient.ConnectAsync(cancellationToken);

var login = gameClient.Api.Shared.Login;
var reply = await login.LoginAsync(new LoginRequest { PlayerName = name });
await gameClient.StartSessionAsync(
    reply.SessionId,
    reply.SessionGeneration,
    cancellationToken);
```

`ConnectAsync` owns the framework handshake and heartbeat startup.
`StartSessionAsync` tells the framework which server-issued session is active;
reliable push replay and acknowledgements remain framework protocol details.
Business RPC services are exposed through `gameClient.Api`.

## Core Client Primitive

Use `LakonaGameClientCore` directly only when you are building a custom client
wrapper instead of using generated `Client.Generated.LakonaGameClient`.

The core primitive owns framework handshake state, heartbeat state, reliable
push client state, opaque resume tickets, and connection snapshots. Platform,
game version, build id, runtime, and capability metadata remain application
concerns. Generated wrappers expose business services through `gameClient.Api`.

Construct `LakonaGameClientOptions` with `Func<ITransport>` for automatic
recovery. The wrapper creates a fresh transport per connection generation while
application-held API/service proxies remain stable. Business code does not save
session ids or call login again during a transient disconnect.

Normal clients should not call reliable-push ack RPCs. The generated wrapper
uses the framework protocol negotiated by handshake. If the server disables
reliable push, the wrapper keeps the same public callback path and treats
notifications as immediate best-effort delivery.

## Engine-neutral session state

`ClientSessionController` is a pure state helper. Unity, Godot, and plain .NET
clients can render their own UI from the snapshot without the framework
touching engine APIs or dispatchers.

```csharp
using Lakona.Game.Client.Sessions;

var controller = new ClientSessionController();
controller.StartSession(sessionId);
controller.MarkReconnecting();

if (controller.Snapshot.Phase == ClientSessionPhase.Reconnecting)
{
    // Render reconnecting UI until the generated client wrapper reports ready again.
}
```
