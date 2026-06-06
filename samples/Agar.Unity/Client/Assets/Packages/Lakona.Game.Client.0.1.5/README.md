# Lakona.Game.Client

`Lakona.Game.Client` contains engine-neutral client helpers for game clients built on top of Lakona.Rpc.

The package focuses on one recommended main entry point, `LakonaGameClient`, plus lower-level reliable server push and reconnect-aware state helpers:

- track the latest applied reliable push sequence
- detect duplicate reliable push messages
- decide whether an incoming push should be applied and acknowledged
- reset sequence state when the client starts a new logical session
- expose an engine-neutral session phase snapshot for reconnect, refresh, and state-lost flows

The library does not depend on Unity, Godot, or any transport package. Game clients remain responsible for choosing their transport, dispatching callbacks onto the engine main thread, and applying business-specific payloads.

## Main Client API

```csharp
using Lakona.Game.Abstractions;
using Lakona.Game.Client;
using Lakona.Game.Client.ReliablePush;
using Lakona.Game.Client.Sessions;

var client = new LakonaGameClient();
client.StartSession(new GameSessionKey(playerId, sessionId, generation), lastReliableSequence: 0);

await client.ProcessReliablePushAsync(
    ReliablePushSequence.From(update.ReliableSequence),
    update,
    applyAsync: static (payload, ct) =>
    {
        // Apply the business payload on the application's chosen thread.
        return ValueTask.CompletedTask;
    },
    acknowledgeAsync: async (ack, ct) =>
    {
        // Send ack.Session.SessionId, ack.Session.Generation, and ack.Sequence.Value through the game's RPC API.
        await playerService.AckReliablePushAsync(ack.Session.SessionId, ack.Session.Generation, ack.Sequence.Value, ct);
        return ReliablePushAckOutcome.Accepted();
    },
    cancellationToken);

if (client.Snapshot.Phase == ClientSessionPhase.RefreshRequired)
{
    // Clear transient view state and fetch an authoritative game snapshot.
}

if (client.Snapshot.Phase == ClientSessionPhase.StateLost)
{
    // Start a new login/session flow. StateLost remains terminal until StartSession is called again.
}
```

## Lower-level reliable push inbox

Use `ReliablePushInbox` directly only when you want to manage session phase separately. Session identity comes from `Lakona.Game.Abstractions.GameSessionKey`.

```csharp
using Lakona.Game.Abstractions;
using Lakona.Game.Client.ReliablePush;

var session = new GameSessionKey(ownerKey: playerId, sessionId: sessionId, generation: generation);
var inbox = new ReliablePushInbox();
inbox.StartSession(session, lastAppliedSequence);

await inbox.ProcessAsync(
    ReliablePushSequence.From(update.ReliableSequence),
    update,
    applyAsync: static (payload, ct) =>
    {
        // Apply the business payload on the application's chosen thread.
        return ValueTask.CompletedTask;
    },
    acknowledgeAsync: async (ack, ct) =>
    {
        // Send ack.Session and ack.Sequence.Value through the game's RPC API.
        await playerService.AckReliablePushAsync(ack.Session.SessionId, ack.Session.Generation, ack.Sequence.Value, ct);
        return ReliablePushAckOutcome.Accepted();
    },
    cancellationToken);
```

## Engine-neutral session state

`ClientSessionController` is a pure state helper. Unity, Godot, and plain .NET clients can render their own UI from the snapshot without the framework touching engine APIs or dispatchers.

```csharp
using Lakona.Game.Abstractions;
using Lakona.Game.Client.ReliablePush;
using Lakona.Game.Client.Sessions;

var controller = new ClientSessionController();
controller.StartSession(new GameSessionKey(playerId, sessionId, generation));
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
