# RPC

Lakona.Rpc is the typed communication foundation under Lakona. It exists so
game code can describe service contracts once, share DTOs between server and
Unity/Godot clients, and let generated glue handle frames, dispatch,
callbacks, and transport differences.

RPC is infrastructure. Business semantics live in contracts and DTOs, not in
transport code.

## Design Principles

### Contracts Own Semantics

RPC service interfaces, method ids, notification contracts, and DTOs live in a
shared assembly. Server and client compile the same contract source so protocol
drift is not a normal workflow.

```csharp
[RpcService(10, NotificationContract = typeof(IPlayerCallback))]
public interface IPlayerService
{
    [RpcMethod(1)]
    ValueTask<LoginReply> LoginAsync(LoginRequest request);
}
```

Stable numeric ids are part of the protocol contract. Do not reuse published
service ids, method ids, or notification ids for different meanings.

### Generated Code Owns Glue

`Lakona.Rpc.Analyzers` reads shared contracts at compile time and emits client
facades, notification binders, server binders, and generated service metadata.

Generated RPC glue is compiler output. New projects must not contain
project-local `Generated/` RPC source folders, codegen scripts, editor
postprocessors, or tool manifests for day-to-day RPC generation.

See [source-generation.md](source-generation.md) for the source-generation
contract.

### Runtime Owns Frames And Sessions

The runtime turns generated method calls into request, response, and push
frames. Server applications should use high-level host configuration and
generated binders. They should not hand-write `RpcSession` loops or
`serviceId:methodId` dispatch dictionaries.

For typed requests, responses, and notifications, the runtime reserves the
envelope header and gives the configured serializer an `IBufferWriter<byte>`
positioned at the business payload. The serializer writes directly into that
final owned frame; it does not allocate a standalone payload frame for the
runtime to copy. Decoded payload and push-metadata bytes remain owned slices of
the received frame for the lifetime of their decoded frame object.

Public API commitment boundaries are documented in
[public-api-boundaries.md](public-api-boundaries.md).

`RpcServerHost` is an embeddable, token-driven runtime owner. It observes the
`CancellationToken` supplied to `RunAsync` and does not subscribe to Ctrl+C,
SIGTERM, process-exit, or another ambient process signal. The application
composition root owns signal adaptation: `LakonaGameServer.RunAsync` uses the
.NET host lifetime, while a standalone RPC console application explicitly
maps its chosen process signals to one shared shutdown token.

The server host also owns hard connection admission. It atomically reserves an
active-connection slot before constructing `RpcSession`; when the finite budget
is full, it closes the newly accepted transport instead of retaining another
Session or wait task. Higher-level frameworks may add neutral Session admission
gates that return a lifetime cancellation token and an exactly-once lease. The
host composes those tokens with shutdown, skips lifecycle notifications for
rejected connections, and releases every admitted lease after Session cleanup.

Each Session also owns a finite request budget: active handlers plus the queued
requests waiting for a concurrency slot. When that budget is full, the receive
loop awaits the `Overloaded` response before reading another application frame.
A stalled response transport therefore applies receive backpressure instead of
creating an unbounded family of overload-send tasks outside the request budget.

Host cancellation starts a cooperative Session drain under one host-wide
shutdown deadline. `RpcServerHostBuilder.UseShutdownTimeout` configures that
deadline; the default is 15 seconds. If active Sessions do not finish in time,
the host aborts their transports and throws `RpcServerShutdownTimeoutException`
instead of reporting a clean stop. Managed code cannot forcibly terminate an
uncooperative handler, so scoped Session state is not disposed concurrently
with that handler; late completion performs normal exactly-once cleanup. The
forced transport-abort join uses the same configured duration as its maximum
cleanup window, so a transport that also refuses disposal cannot restore an
unbounded wait. The application composition root treats the timeout as terminal
and owns any final process-termination policy.

Protocol-specific meanings such as "Game Handshake complete" remain above RPC.
RPC supplies the enforcement and cancellation mechanism; Lakona.Game owns its
pending-handshake capacity, deadline, state transition, and defaults.

### Transport And Serializer Are Replaceable

Transports and serializers are extension points. Gameplay code should not care
whether the connection uses TCP, WebSocket, KCP, loopback, JSON, or MemoryPack.

Custom transports, connection acceptors, and serializers belong behind stable
extension interfaces such as `ITransport`, `IRpcConnectionAcceptor`, and
`IRpcSerializer`.

The KCP server listener shares one UDP receive loop across connections, but it
must not eagerly drain decoded KCP messages into a separate application frame
queue. Datagram input remains in KCP's bounded per-connection receive window
until that connection's `ReceiveFrameAsync` caller requests the next frame. A
slow RPC Session therefore closes its advertised KCP receive window without
blocking the shared listener, retaining an unbounded number of decoded frames,
or delaying unrelated connections.

The Loopback transport models one connection pair with one shared lifecycle
owner. Each direction uses a bounded frame queue with wait-based backpressure;
callers may select a smaller capacity for deterministic pressure tests.
Disposing either endpoint closes both directions, wakes pending I/O, rejects
new sends, and releases queued owned frames. Loopback must not report one peer
connected after the other peer has closed.

KCP background faults are terminal at their smallest owner. An unexpected
listener receive-loop failure closes the listener's accept boundary with the
original cause so endpoint supervision can stop cleanly. A scheduled update
failure removes only that connection's registration, transitions its transport
to disconnected, and wakes pending receive work with the original cause.
Schedulers do not retry or log transport failures; RPC Session and host owners
provide the single diagnostic boundary.

KCP update scheduling follows the protocol's `Check` deadline instead of
unconditionally queuing every connection on each scheduler scan. `Send`
submits the next deadline after its immediate update, while datagram input
invalidates the previous deadline and makes that connection due again. Each
registration remains isolated and non-overlapping, so a delayed update cannot
serialize unrelated connections behind it.

`IRpcSerializer.Serialize<T>` is writer-first: implementations synchronously
write only the serialized DTO bytes to the supplied `IBufferWriter<byte>` and
must not complete, dispose, or retain that writer. `SerializeFrame` is a Core
convenience extension for callers that explicitly need a standalone owned
payload frame; normal runtime request, response, and notification paths do not
use it.

### Callback Is Part Of The Contract

Server-to-client push is modeled through notification contracts. A callback
contract is not a separate event bus; it is the reverse direction of the same
typed RPC session.

A notification contract is an interface marked with the parameterless
`[RpcNotificationContract]` marker. The owning service declares its
notification contract through `RpcServiceAttribute.NotificationContract`,
which is the single association authority between a service and its callback.
The marker itself carries no service pointer:

```csharp
[RpcService(10, NotificationContract = typeof(IPlayerCallback))]
public interface IPlayerService
{
    // RPC methods
}

[RpcNotificationContract]
public interface IPlayerCallback
{
    [RpcNotification(1)]
    void OnMatchmakingStatus(MatchmakingStatusUpdate update);
}
```

For replayable game notifications above RPC callbacks, publish notification
intent through the Lakona game session APIs. Lakona.Game business handlers do
not receive the connection-scoped callback proxy directly; the game framework
owns reliable push sequencing, acknowledgement, and replay policy.

### Framework Status Is Not Business Failure

`RpcStatus` describes framework outcomes such as missing handlers, handler
failure, overload, bad request, or protocol error. Business failures such as
login rejection, room full, invalid move, or cooldown not ready belong in
business DTOs.

See [status-error-model.md](status-error-model.md)
for status semantics.

## Maintainer References

- [source-generation.md](source-generation.md)
- [wire-protocol-v1.md](wire-protocol-v1.md)
- [status-error-model.md](status-error-model.md)
- [public-api-boundaries.md](public-api-boundaries.md)
