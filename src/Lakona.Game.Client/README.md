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

```csharp
using Lakona.Game.Abstractions;
using Lakona.Game.Client;
using Lakona.Game.Client.ReliablePush;
using Lakona.Game.Client.Sessions;

var core = new LakonaGameClientCore();
core.StartSession(sessionId, lastReliableSequence: 0);

await core.ProcessReliablePushAsync(
    ReliablePushSequence.From(update.ReliableSequence),
    update,
    applyAsync: static (payload, ct) =>
    {
        // Apply the business payload on the application's chosen thread.
        return default;
    },
    acknowledgeAsync: async (ack, ct) =>
    {
        // Send ack.Sequence.Value through the game's RPC API. Use your own client-facing
        // session token or stream id if the server requires one for acknowledgement.
        await playerService.AckReliablePushAsync(sessionId, ack.Sequence.Value, ct);
        return ReliablePushAckOutcome.Accepted();
    },
    cancellationToken);

if (core.Snapshot.Phase == ClientSessionPhase.RefreshRequired)
{
    // Clear transient view state and fetch an authoritative game snapshot.
}

if (core.Snapshot.Phase == ClientSessionPhase.StateLost)
{
    // Start a new login/session flow. StateLost remains terminal until StartSession is called again.
}
```

## Lower-level reliable push inbox

Use `ReliablePushInbox` directly only when you want to manage session phase
separately. The session id is an opaque client-side key chosen by your game
protocol, not `Lakona.Game.Server.Sessions.GameSessionKey`.

```csharp
using Lakona.Game.Abstractions;
using Lakona.Game.Client.ReliablePush;

var inbox = new ReliablePushInbox();
inbox.StartSession(sessionId, lastAppliedSequence);

await inbox.ProcessAsync(
    ReliablePushSequence.From(update.ReliableSequence),
    update,
    applyAsync: static (payload, ct) =>
    {
        // Apply the business payload on the application's chosen thread.
        return default;
    },
    acknowledgeAsync: async (ack, ct) =>
    {
        // Send ack.SessionId and ack.Sequence.Value through the game's RPC API.
        await playerService.AckReliablePushAsync(ack.SessionId, ack.Sequence.Value, ct);
        return ReliablePushAckOutcome.Accepted();
    },
    cancellationToken);
```

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
