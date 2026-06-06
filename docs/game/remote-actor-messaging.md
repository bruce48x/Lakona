# Managed Distributed Actor Messaging

Lakona.Game managed actor messaging should feel close to skynet's local and cluster call model: actor calls use the same business method shape, and the target selector makes placement intent explicit.

The recommended API shape is generated from server-side actor classes:

```csharp
public readonly record struct RoomId(string Value);

public sealed class RoomActor : Actor<RoomId>
{
    public ValueTask<JoinRoomReply> JoinAsync(
        JoinRoomRequest request,
        CancellationToken cancellationToken = default)
    {
        // actor mailbox code
    }
}
```

The source generator emits typed accessors:

```csharp
public sealed class RoomActors
{
    public RoomRef Get(RoomId roomId);

    public RoomLocalRef Local(RoomId roomId);

    public RoomRemoteRef Remote(NodeId nodeId, RoomId roomId);
}
```

Business code uses distributed access by default:

```csharp
var reply = await _rooms
    .Get(roomId)
    .JoinAsync(request, cancellationToken);
```

Use explicit selectors when the call must be constrained:

```csharp
var localReply = await _rooms
    .Local(roomId)
    .JoinAsync(request, cancellationToken);

var pinnedReply = await _rooms
    .Remote(nodeId, roomId)
    .JoinAsync(request, cancellationToken);
```

`Get(roomId)` checks the local runtime first, then `ActorDirectory` placement. `Local(roomId)` is current-process only. `Remote(nodeId, roomId)` targets the specified node and does not query placement.

## Design Goals

- Ordinary game server developers should not hand-write actor ids, route keys, message kinds, serializers, dispatch switches, endpoint addresses, or reply-correlation plumbing.
- Business layer code should not know endpoint addresses, `clusterName`, `endpointName`, route-directory endpoints, or actor-directory host endpoints.
- Actor calls should differ only in target selection, not in every business method call.
- Distributed messaging must keep target selection explicit. Lakona.Game should not expose an unqualified transparent actor proxy that hides placement policy.
- Failures should throw typed actor call exceptions. Ordinary business code should not switch over `RemoteAskResult` or `RemoteActorInvocationResult`.
- Repeated wrapper code should be generated at compile time. Source generation adds no runtime reflection or dynamic dispatch requirement.
- Server-internal actor contracts belong in server assemblies, not in the client-facing `Shared` project.

## Generated API

For each eligible `Actor<TKey>` subclass in a server-side assembly, the generator emits one actor accessor group:

```csharp
public sealed class RoomActors
{
    public RoomRef Get(RoomId id);

    public RoomLocalRef Local(RoomId id);

    public RoomRemoteRef Remote(NodeId node, RoomId id);
}

public readonly struct RoomRef
{
    public ValueTask<JoinRoomReply> JoinAsync(
        JoinRoomRequest request,
        CancellationToken cancellationToken = default);
}

public readonly struct RoomLocalRef
{
    public ValueTask<JoinRoomReply> JoinAsync(
        JoinRoomRequest request,
        CancellationToken cancellationToken = default);
}

public readonly struct RoomRemoteRef
{
    public ValueTask<JoinRoomReply> JoinAsync(
        JoinRoomRequest request,
        CancellationToken cancellationToken = default);
}
```

The generated `Get(...)` ref invokes the process-local actor when present. Otherwise it resolves placement through `ActorDirectory`, uses cached placement when valid, and sends the call to the resolved owner node.

The generated `Local(...)` ref invokes the process-local `IActorRuntime`.

The generated `Remote(nodeId, ...)` ref sends to the specified node and does not query `ActorDirectory`.

The business method surface is intentionally not doubled with `TryJoinAsync` or `TryLeaveAsync`. Normal actor calls return normally or throw. Lower-level result-returning APIs remain available for framework internals and rare boundary services.

## Actor Key Model

Actor key type is declared in the actor base type:

```csharp
public sealed class RoomActor : Actor<RoomId>
{
}
```

This avoids separate `[ActorKey]` attributes and avoids generator guessing. The generator uses `TKey` to type `Get(TKey id)`, `Local(TKey id)`, and `Remote(NodeId nodeId, TKey id)`.

Default key-to-string conversion:

1. If `TKey` has a readable `Value` property, use `Value.ToString()`.
2. Otherwise use `TKey.ToString()`.

Default actor id shape:

```txt
<actor-name>/<key-value>
```

Long-lived protocols can pin the wire name and method ids with `[ActorName]` and `[ActorMethod]`.

## Failure Model

Generated business methods return a reply on success and throw typed exceptions on local or distributed failure.

```csharp
try
{
    var reply = await _rooms
        .Get(roomId)
        .JoinAsync(request, cancellationToken);
}
catch (ActorCallException ex) when (ex.Status == ActorCallStatus.ActorNotFound)
{
    // room has gone away or was never registered
}
```

`ActorCallException` and derived exceptions carry structured failure details such as status, node, actor id, actor name, method name, and correlation id. Initial status values should cover route not found, expired, timeout, backpressure, handler unavailable, node unavailable, serialization failure, deserialization failure, and cancellation.

The generated API should not require ordinary call sites to switch over these statuses. Boundary services that need status-returning behavior can use the lower-level invoker.

## Runtime Layers

The generated typed API sits above existing cluster primitives:

```txt
game service code
  -> generated RoomActors.Get/Local/Remote refs
  -> ActorDirectory cache / local actor invoker / remote actor invoker
  -> IActorRuntime / IClusterRouter
  -> ClusterActorEnvelope
  -> ClusterMessage / RouteLocation / transport adapter
```

`ActorDirectory` lives in `Lakona.Game.Server`. The first distributed version finds the directory host through cluster feature discovery and caches both the directory host and actor placement. Business code does not receive endpoint addresses or directory endpoint names.

The lower-level `ClusterMessage`, `ClusterActorEnvelope`, `IClusterRouter`, and remote actor invoker remain important. They are implementation foundations and escape hatches, not the recommended daily business API.

## Target Selectors

`Get(id)` is the default business-facing selector. It never creates actors and should not auto-retry business actor calls after the request reaches an actor. Placement lookup retry is infrastructure policy; business method execution remains single-intent.

`Local(id)` uses the process-local actor runtime. Generated local refs should avoid serialization and cluster envelope allocation.

`Remote(nodeId, id)` uses the cluster layer and does not query `ActorDirectory`. It serializes the request, sends a cluster actor envelope through the remote actor invoker to the specified node, waits for correlated replies when needed, deserializes replies, and throws typed actor call exceptions for delivery or reply failures.

## Managed Lifecycle

All actors are framework-managed in the first version. Do not introduce a `UserManaged`/`ActorLifetime` split until a concrete repeated need exists.

Generated lifecycle operations are local-only:

```csharp
await _rooms.SpawnAsync(roomId, request, cancellationToken);
await _rooms.DestroyAsync(roomId, cancellationToken);
```

Spawn claims placement in `ActorDirectory`, creates the actor locally, and invokes the spawn hook if present; hook or local creation failure unregisters placement and rolls back the local actor. Destroy unregisters placement first, then invokes the destroy hook if present and removes the local actor; hook or stop failure attempts to re-register placement for the still-local actor. Lakona.Game does not provide `SpawnRemoteAsync` or `DestroyRemoteAsync`; cross-node creation or destruction should be explicit business commands to a manager actor or service on the target node.

## Server-Side Boundary

Managed actor generation is server-side infrastructure. It should scan server assemblies and generate server-only code.

Do not place actor declarations in the client-facing `Shared` project. `Shared` remains for client/server DTOs and RPC contracts. If a request or reply DTO is also needed by the client, that DTO can live in `Shared`; the actor class, generated actor refs, actor attributes, route keys, and invoker types stay server-side.

## Relation To Low-Level APIs

The lower-level `AskRemoteAsync`, `TellRemoteAsync`, and remote invoker APIs prove the plumbing for cluster actor envelopes, reply correlation, and dispatcher composition. They are too low-level for frequent business use because callers must provide actor id strings, method kind strings, serialization delegates, reply deserialization delegates, and timeouts at every call site.

The managed generated API should replace those extensions as the recommended documentation path. The lower-level APIs can remain as escape hatches for infrastructure code and boundary services.
