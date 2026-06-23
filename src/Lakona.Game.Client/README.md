# Lakona.Game.Client

`Lakona.Game.Client` contains reusable engine-neutral Game client primitives.
Generated game projects use a project-specific `Rpc.Generated.LakonaGameClient`
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
using Rpc.Generated;

await using var gameClient = new LakonaGameClient(options, callbackReceiver);
await gameClient.ConnectAsync(cancellationToken);

var login = gameClient.Api.Shared.Login;
var reply = await login.LoginAsync(new LoginRequest { PlayerName = name });
```

`ConnectAsync` owns the framework handshake and heartbeat startup. Business RPC
services are exposed through `gameClient.Api`.

## Core Client Primitive

Use `LakonaGameClientCore` directly only when you are building a custom client
wrapper instead of using generated `Rpc.Generated.LakonaGameClient`.

The core primitive owns framework handshake state, resolved server
capabilities, heartbeat state, reliable push client state, and connection
snapshots. It does not expose business services; generated wrappers expose
business services through `gameClient.Api`.

Normal clients should not call reliable-push ack RPCs. The generated wrapper
uses the framework protocol negotiated by handshake. If the server disables
reliable push, the wrapper keeps the same public callback path and treats
notifications as immediate best-effort delivery.

## Lower-level reliable push inbox

Use `ReliablePushInbox` directly only when you want to manage session phase
separately while building framework or generated-wrapper infrastructure. It is
not a normal game-client entry point.

`ReliablePushInbox` can decide whether a sequence should be applied and whether
an acknowledgement is required. Generated wrappers are responsible for wiring
that acknowledgement to the framework protocol. Game code should receive
business callbacks after the wrapper has handled sequencing concerns.

## Engine-neutral session state

`ClientSessionController` is a pure state helper. Unity, Godot, and plain .NET
clients can render their own UI from the snapshot without the framework
touching engine APIs or dispatchers.

```csharp
using Lakona.Game.Abstractions;
using Lakona.Game.Client.ReliablePush;
using Lakona.Game.Client.Sessions;

var controller = new ClientSessionController();
controller.StartSession(sessionId);
controller.MarkReconnecting();

controller.ApplyAckOutcome(ReliablePushAckOutcome.StateRefreshRequired());

if (controller.Snapshot.Phase == ClientSessionPhase.RefreshRequired)
{
    // Clear transient view state and fetch an authoritative game snapshot.
}

controller.ApplyAckOutcome(ReliablePushAckOutcome.StateLost());

if (controller.Snapshot.Phase == ClientSessionPhase.StateLost)
{
    // Start a new login/session flow. StateLost remains terminal until StartSession is called again.
}
```
