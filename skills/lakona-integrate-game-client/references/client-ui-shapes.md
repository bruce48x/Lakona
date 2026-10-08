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

## Unity Presentation Loop

Run state rendering on the main thread. This partial presenter assumes an
existing client owner; helper methods below are application UI methods.

```csharp
private LakonaGameConnectionState? _lastConnectionState;

private void Update()
{
    ApplyPendingCallbacks(); // Drain the project's thread-safe callback inbox.
    if (_gameClient == null)
        return;

    var state = _gameClient.ConnectionState;
    if (_lastConnectionState == state)
        return;
    _lastConnectionState = state;

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

Reset `_lastConnectionState` when replacing the owner so a fresh client in the
same enum state still refreshes the UI. Gate business controls using connection,
authentication, world readiness, and pending-request state together. Recheck
ownership when applying queued callbacks or async results; a callback from an
old client must not update the new login. Do not retry a non-idempotent business
request merely because connection state returns to `Connected`.

Use the equivalent main-thread presenter/update mechanism in Godot or another
engine. `ConnectionState` is framework observation; it does not own engine UI.
