# Client Connection UI Shapes

Adapt these shapes to the discovered generated namespace, callback receiver,
business contracts, transport, serializer, and engine dispatcher.

## Recovery-capable Construction

```csharp
// createTransport returns a NEW ITransport with the same endpoint/security setup.
Func<ITransport> createTransport = CreateConfiguredTransport;
var options = new LakonaGameClientOptions(createTransport, serializer);
var gameClient = new LakonaGameClient(options, callbackReceiver);
await gameClient.ConnectAsync(cancellationToken);
// Now invoke the project's login RPC through gameClient.Api.
// Only its successful business response establishes authenticated UI state.
```

Use `Lakona.Game.Client` and `Lakona.Rpc.Core` imports plus the project's
generated client namespace. `CreateConfiguredTransport` is application code,
not a framework method. The owner retains `gameClient` until logout/shutdown
and awaits `DisposeAsync` there; a short-lived login method must not dispose
the client on return.

## Unity Event Presentation

Run state rendering on the main thread. This partial presenter assumes an
existing client owner; helper methods below are application UI methods. Add
`System`, `System.Collections.Concurrent`, and `Lakona.Game.Client` imports.

```csharp
private readonly ConcurrentQueue<(LakonaGameClient Owner, LakonaGameConnectionStateChange Change)> _connectionChanges
    = new ConcurrentQueue<(LakonaGameClient, LakonaGameConnectionStateChange)>();
private Action<LakonaGameConnectionStateChange> _stateChanged;

// Call on the main thread for the new owner, before ConnectAsync.
private void AttachConnectionState(LakonaGameClient client)
{
    _stateChanged = change => _connectionChanges.Enqueue((client, change));
    client.ConnectionStateChanged += _stateChanged;
    RenderConnectionState(client.ConnectionState);
}

// Call on the main thread before discarding or replacing the current owner.
private void DetachConnectionState()
{
    if (_gameClient != null && _stateChanged != null)
        _gameClient.ConnectionStateChanged -= _stateChanged;
    _stateChanged = null;
}

private void Update()
{
    ApplyPendingCallbacks(); // Drain the project's thread-safe callback inbox.
    while (_connectionChanges.TryDequeue(out var item))
    {
        if (ReferenceEquals(item.Owner, _gameClient))
            RenderConnectionState(item.Change.CurrentState);
    }
}

private void RenderConnectionState(LakonaGameConnectionState state)
{
    switch (state)
    {
        case LakonaGameConnectionState.Created:
            ShowConnectionStatus("Ready to connect");
            break;
        case LakonaGameConnectionState.Connecting:
            ShowConnectionStatus("Connecting");
            break;
        case LakonaGameConnectionState.Connected:
            ShowConnectionStatus("Connected");
            // Login and world readiness remain owned by business responses.
            break;
        case LakonaGameConnectionState.Reconnecting:
            ShowConnectionStatus("Reconnecting");
            // Keep the current match/world until recovery or terminal failure.
            break;
        case LakonaGameConnectionState.Disconnected:
            ShowConnectionStatus("Recovery failed");
            break;
        case LakonaGameConnectionState.Disposed:
            ShowConnectionStatus("Connection closed");
            break;
    }
}
```

Detach the handler before replacing the owner and attach to the new one. Set
`_gameClient` to the new owner (or null) before draining queued events. Queued
notifications from the old owner are ignored. Gate business controls using connection,
authentication, world readiness, and pending-request state together. Recheck
ownership when applying queued callbacks or async results; a callback from an
old client must not update the new login. Do not retry a non-idempotent business
request merely because connection state returns to `Connected`.

Use the equivalent main-thread presenter/update mechanism in Godot or another
engine. `ConnectionState` is framework observation; it does not own engine UI.
For older clients without the event, query `ConnectionState` from the existing
main-thread presenter and render when it changes. For controls that require the
latest availability, read the current property rather than a queued transition.
