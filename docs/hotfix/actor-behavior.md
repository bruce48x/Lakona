# Hotfix Actor Behavior

Hotfix actor behavior is the reloadable business layer for stable actor state.
`Server.App` defines actor classes and state fields. `Server.Hotfix` defines
extension methods and lifecycle hooks that run inside actor turns.

## Behavior Methods

```csharp
[HotfixBehaviorOf(typeof(RoomActor))]
public sealed partial class RoomBehavior
{
    public ValueTask<JoinRoomReply> JoinAsync(
        RoomActor room,
        JoinRoomRequest request,
        CancellationToken cancellationToken = default)
    {
        room.JoinedPlayers.Add(request.PlayerId);
        return new ValueTask<JoinRoomReply>(
            new JoinRoomReply { PlayerCount = room.JoinedPlayers.Count });
    }
}
```

Behavior methods should mutate only the target actor state and use generated
actor selectors for calls to other actors.

Public instance methods define the remotely callable Actor API by default.
Use `[ActorMethod("join")]` when the wire identity must remain stable while the
C# method name evolves. Generated method keys and ids use `"join"`; direct
selectors still point to the C# method, so refactoring remains navigable.

Use `[ActorIgnore]` on public instance helpers that exist only for ordinary C#
composition, such as lifecycle setup or teardown methods. Ignored methods are
removed before Actor method-shape validation and are never generated or
remotely dispatched:

```csharp
[ActorMethod("join")]
public ValueTask<JoinRoomReply> JoinAsync(/* ... */) => /* ... */;

[ActorIgnore]
public ValueTask StartTimerAsync(RoomActor room) => /* ... */;
```

An Actor method cannot declare both attributes. An `[ActorMethod]` name must
also be non-empty; invalid declarations fail generation and runtime scanning
instead of silently changing the protocol surface.

## Actor State Access

Stable actor fields and properties should be `internal` unless they are an
intentional public contract. The stable App assembly grants its Hotfix assembly
internal visibility so behavior code can compile across the assembly seam.

The Hotfix analyzer then narrows that assembly-wide visibility: a non-public
field or property declared by an `Actor<TKey>` may only be referenced by the
actor itself or by the unique class whose
`[HotfixBehaviorOf(typeof(ActorType))]` targets that actor. Access from service,
lifecycle, helper, or another actor's behavior code produces `LKNHOTFIX031` as
a build error. Explicitly public actor fields and properties are not restricted
by this diagnostic.

## Lifecycle Hooks

Use `[ActorStart]` and `[ActorStop]` for startup and cleanup. Long-lived runtime
handles such as timers should be stored in stable actor state and destroyed
during cleanup with a noncancelable cleanup token when required.

## Placement

Generated selectors make placement intent explicit:

```csharp
await actors.Route<RoomActor>(roomId).CallAsync(static behavior => behavior.JoinAsync, request, ct);
await actors.Local<RoomActor>(roomId).PostAsync(static behavior => behavior.RunTickAsync, request, ct);
```

The selector must be a direct static lambda in the form
`static behavior => behavior.MethodAsync`. This keeps Go to Definition pointed
at the implementation and lets the runtime bind the method against the active
hotfix generation without retaining the old generation. Resolving that method
identity does not construct the Behavior on the calling node; only the node
which owns and executes the Actor needs the Behavior's application dependencies.

Business services should not use transport callbacks, session callback objects,
or hand-written string dispatch as actor state.
