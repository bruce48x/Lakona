# Hotfix Actor Behavior

Hotfix actor behavior is the reloadable business layer for stable actor state.
`Server.App` defines actor classes and state fields. `Server.Hotfix` defines
extension methods and lifecycle hooks that run inside actor turns.

## Behavior Methods

```csharp
[HotfixBehaviorOf(typeof(RoomActor))]
public static partial class RoomBehavior
{
    public static ValueTask<JoinRoomReply> JoinAsync(
        this RoomActor room,
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
await rooms.Route(roomId).CallAsync(RoomBehavior.JoinAsync, request, ct);
await rooms.Local(roomId).PostAsync(RoomBehavior.RunTickAsync, request, ct);
```

Business services should not use transport callbacks, session callback objects,
or hand-written string dispatch as actor state.
