# Lakona Actor Shapes

Use repository evidence as the final authority. Lakona actors split stable
state from reloadable behavior.

## Stable App Shape

Declare a business key and an actor state shell in `Server.App`:

```csharp
using Lakona.Game.Server;

public readonly record struct RoomId(string Value);

[NodeRole("battle")]
public sealed class RoomActor : Actor<RoomId>
{
    internal readonly HashSet<string> Members = new(StringComparer.Ordinal);
    internal RoomPhase Phase;
}
```

Prefer current project visibility conventions. The Hotfix assembly must be able
to reference the actor type, while mutable fields should normally remain
`internal`. Do not expose fields publicly unless they are an intentional public
contract.

Put request, reply, lifecycle, and timer DTOs that cross the stable/Hotfix or
node boundary in the stable App assembly. Follow the project's selected cluster
serializer. A routed actor request and reply must be serializable by the
cluster serializer even when the client endpoint uses a different serializer.

Do not create a separate actor contract interface unless the detected Lakona
version or project convention requires it. Current generated selectors derive
the callable surface from public Hotfix behavior methods.

Every stable Actor declares exactly one `[NodeRole]`. A process can host the
Actor only when that role appears in `Lakona:Node:Roles`; the same declaration
also limits Startup Actor replicas and ordinary placement. Choose the role from
the product topology, not from the caller, connection string, or current node.

## Hotfix Behavior Shape

Bind exactly one behavior class to the stable actor:

```csharp
[HotfixBehaviorOf(typeof(RoomActor))]
public sealed partial class RoomBehavior
{
    public ValueTask<JoinRoomReply> JoinAsync(
        RoomActor self,
        JoinRoomRequest request,
        CancellationToken cancellationToken = default)
    {
        self.Members.Add(request.PlayerId);
        return new ValueTask<JoinRoomReply>(
            new JoinRoomReply { Accepted = true });
    }
}
```

Use public behavior methods as the generated actor API. Mutate only the target
actor's state during its turn. Use constructor injection for actors, stores,
notifiers, loggers, and generation-scoped policy helpers needed by the
behavior.

Follow repository `ValueTask` rules. Return `default` for a completed
non-generic operation and `new ValueTask<T>(value)` for an immediately
available reply when those are the established conventions.

## Actor Keys

Choose keys from stable product identity:

```text
user/player-123
room/room-456
matchmaking/default
leaderboard/current
```

The key type belongs in `Actor<TKey>`. The default actor ID uses the actor name
and key value. Use `[ActorName]` and `[ActorMethod]` only when a long-lived wire
contract must pin names or numeric IDs.

Do not use node IDs, transport addresses, session callback objects, or
connection IDs as actor identity. A connection ID may appear in request data
when the behavior genuinely needs it; it must not determine distributed actor
ownership accidentally.

## Select Actor Access

Use a direct static selector lambda:

```csharp
var reply = await actors
    .Route<RoomActor>(roomId)
    .CallAsync(
        static behavior => behavior.JoinAsync,
        request,
        cancellationToken);

await actors
    .Place<RoomActor>(roomId)
    .EnsureAsync(cancellationToken);

await actors
    .Place<RoomActor>(roomId)
    .DestroyAsync(cancellationToken);

await actors
    .Local<RoomActor>(roomId)
    .PostAsync(
        static behavior => behavior.RunTickAsync,
        request,
        cancellationToken);
```

Selection rules:

- `Route`: resolve normal business ownership and permit a remote owner.
- `Local`: invoke only the current process after ownership is already proven.
- `Place`: manage a logical actor through cluster-aware placement and
  lifecycle.
  `CreateAsync` requires the activation to be absent; `EnsureAsync` returns an
  existing activation or creates one; `DestroyAsync` retires the exact
  activation resolved by the operation and is idempotent when absent.
  Placement never relocates an existing activation.
- `Startup`: select one replica from a group declared by
  `[HotfixConfigureActors]` and `RegisterStartup<TActor, TKey>`.
- Direct `self`: continue work on the actor already executing the current turn.

Do not capture a behavior delegate. The static selector lets reload bind the
method on the active Hotfix generation.

## Lifecycle And Hosting

Put reloadable lifecycle work on the behavior:

```csharp
[ActorStart]
public ValueTask StartAsync(RoomActor self, ActorStartCall call)
{
    return InitializeAsync(self, new InitializeRoomRequest(), call.CancellationToken);
}

[ActorStop]
public ValueTask StopAsync(RoomActor self, ActorStopCall call)
{
    return CleanupAsync(self, new CleanupRoomRequest(), call.CleanupCancellationToken);
}
```

Use generated `ActorAccess.Place<TActor>(key).CreateAsync` or `EnsureAsync` for
business-initiated provisioning. An external coordinator may use
`Place<TActor>(key).DestroyAsync()` when it owns the lifecycle decision. An
actor that completes its own business lifetime calls
`self.Context.RequestDeactivation()` during the active turn; the framework
starts destruction only after that turn succeeds, avoiding a self-mailbox
deadlock.

`ActorHosting` is the framework's internal current-node activation transaction
owner. Business code must not inject it, expose raw current-node destruction,
or manually publish or clear actor routes.

Use startup declarations only for fixed replicated services such as a
matchmaking queue. Register them from a `[HotfixStartup]` method marked
`[HotfixConfigureActors]`. Do not treat a startup selector key as an actor ID.

## Failure And Validation

Handle expected `ActorCallException` statuses at a boundary that can make a
product decision. Do not turn `ActorNotFound`, timeout, backpressure, routing,
or serialization failures into silent success.

Validate:

- state transition and reply behavior
- sequential actor turn assumptions
- start and cleanup paths
- missing actor and cancellation behavior
- local versus routed placement intent
- `[NodeRole]` eligibility on the intended node types
- serialization for remote message DTOs
- no blocking waits, discarded required calls, or self-calls
