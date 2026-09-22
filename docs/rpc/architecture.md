# RPC

Lakona.Rpc is the typed communication foundation under Lakona. It exists so
game code can describe service contracts once, share DTOs between server and
Unity/Godot clients, and let generated glue handle frames, dispatch,
callbacks, and transport differences.

RPC is infrastructure. Business semantics live in contracts and DTOs, not in
transport code.

## Ordered Message Entry

Each client connection admits pushes and responses to one FIFO. Dispatch starts
one synchronous processing segment at a time. An incomplete notification await
allows the next message to enter; asynchronous business completion is not
serialized. A synchronous handler blocks later message entry. A push handler may
await another RPC on the same connection without stopping response dispatch.

The runtime binds the synchronization context at startup, or supplies a serial
context when no host context exists. Generated Game clients retain that binding
across reconnects. For an already-awaited native RPC, push entry and the user's
first synchronous segment after the response follow received frame order:
`P1, P2, R, P3, P4`. Calling from a different host context, converting to Task,
adding async wrappers, deferring the await, or explicitly switching execution
contexts does not extend this guarantee across those scheduling boundaries.

Pending calls return standard ValueTask results directly from single-use result
sources. Request sending does not delay result subscription. Generated void
calls use the framework's result adapter instead of additional async wrappers.
Cancellation, timeout, and disconnect remain local completion events, not ordered
server response frames. Duplicate or orphan responses do not invoke user code.

Each server connection has a FIFO request-start queue. Framework admission gates
run in that order; after a service method yields, the next admitted request may
start subject to the existing in-flight budget. Completion and response order
between independent requests can differ from request order. Each connection also
has one FIFO writer shared by notifications, responses, and keepalive sends;
send completion means the actual transport write completed.

Game notification publication participates in a
[request response barrier](../session.md#notification-and-response-publication-order).
FIFO transport writing alone cannot order
notifications still waiting in a higher-level delivery queue.

Client startup links the caller's initial-connect cancellation with runtime
shutdown. Disposal cancels and joins an outstanding connection attempt before
releasing the transport; a late successful connect cannot start background loops.
Concurrent disposal calls share the same cleanup completion.

Client receive and keepalive loops share a connection lifetime. Failure in either
stops and joins both before `Disconnected` is raised, preserving the original
failure. Connection termination rejects new calls and drains already-received
messages in order before failing remaining calls. Custom transports must cooperate
with I/O cancellation.

Explicit client disposal stops framework intake and dispatch and cancels pending RPCs.
It does not wait for arbitrary business handlers, including the handler calling
DisposeAsync itself. An already-started handler retains its frame until it exits;
queued context callbacks are canceled without returning memory still in use by
a running handler. Await disposal asynchronously on an engine thread.

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

The [source-generation contract](source-generation.md) defines generated output,
project configuration, and the boundary between typed glue and Game client
runtime behavior.

### Runtime Owns Frames And Sessions

The runtime turns generated method calls into request, response, and push
frames. Server applications should use high-level host configuration and
generated binders. They should not hand-write `RpcSession` loops or
`serviceId:methodId` dispatch dictionaries.

All server request handlers resolve through `RpcServiceRegistry`. Generated
typed binders and raw handlers share the same admission, response publication,
error handling, logging, and frame ownership path. Low-level dispatch tests
register handlers in that registry as well.

Registrations reject duplicate method ids. Connection-scoped activation uses
single-publication semantics. Factory-created services are released after
in-flight requests drain; explicitly bound singleton instances remain
caller-owned. `RpcServiceRegistration<TService>` owns typed payload
serialization, activation, invocation, and response encoding. Framework control
protocols that own their codec use `RpcRawHandler` and `RpcRawResult`.
Raw handlers may write their opaque payload directly into an envelope-owned
buffer without an intermediate payload-frame copy.

#### Frame Ownership

For typed requests, responses, and notifications, the runtime reserves the
envelope header and gives the configured serializer an `IBufferWriter<byte>`
positioned at the business payload. The serializer writes directly into that
final owned frame; it does not allocate a standalone payload frame for the
runtime to copy. Decoded payload and push-metadata bytes remain owned slices of
the received frame for the lifetime of their decoded frame object.

Each non-empty `TransportFrame` instance owns one lease over its shared buffer.
`Slice` creates an independent lease, and disposing either handle releases only
that handle once. Access through a disposed non-empty handle throws
`ObjectDisposedException`; it cannot read or create further slices, while
other live slices remain valid. The shared `TransportFrame.Empty` value owns no
buffer and remains reusable after disposal.

Frame size enforcement follows the shared envelope budget and derived transport
limits defined in [Resource Limits](wire-protocol-v1.md#resource-limits).

Public API commitment boundaries are documented in
[public-api-boundaries.md](public-api-boundaries.md).

#### Host And Session Lifetime

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
`OnSessionDisconnectedAsync` runs only after the Session, transport, and
admission leases have been released and the active-capacity slot returned.
Session receive, keepalive, and request work has finished before this terminal
signal, so framework observers do not race Session-owned work.
Official transports do not expose a second application-admission callback
before this host seam. In particular, the KCP bootstrap only establishes a
bounded transport connection; application and framework policy belongs to the
host's Session admission gates.

The host's bounded acceptor wrapper owns every accepted connection until it is
handed to the host loop. An unexpected inner accept failure completes that
accept interface with the original cause; `AcceptAsync` is the single runtime
failure path, while `DisposeAsync` separately reports cleanup failures only
after the inner acceptor and every buffered connection have been released.

Each Session also owns a finite request budget: active handlers plus the queued
requests waiting for a concurrency slot. When that budget is full, the receive
loop awaits the `Overloaded` response before reading another application frame.
A stalled response transport therefore applies receive backpressure instead of
creating an unbounded family of overload-send tasks outside the request budget.

Session request gates fail closed. Denial and exception classification follow
the [Status and Error Model](status-error-model.md#implementation-mapping).

Request telemetry follows the Session ownership boundary and covers response
publication or another terminal outcome. Metric definitions, queue timing, and
attribute restrictions are documented in
[Observability](../observability.md#instrumentation-scopes).

Session completion covers all work owned by that Session: the receive loop,
keepalive probing, and in-flight requests. A receive-loop exit cancels and joins
keepalive before scoped state or the transport is released. Conversely, an
unexpected keepalive failure cancels the receive loop and becomes the terminal
disconnect reason. No Session-owned task handle is discarded while its task can
still access Session resources.

Host cancellation or failure starts a cooperative Session drain under one host-wide
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

When the host has already failed, it preserves the original exception after
cleanup and logs any additional shutdown failure, including a drain timeout.
The host owns each accepted transport until a Session takes ownership; admission
cancellation, rejection, and failure all dispose the transport and release any
previously acquired admission leases without emitting Session lifecycle events.

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
or delaying unrelated connections. The listener also does not invoke arbitrary
application admission while receiving datagrams, so new handshakes cannot hold
up traffic for established connections.

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

KCP client bootstrap is finite even when callers do not supply a cancellation
token. One connection attempt owns a ten-second deadline and retransmits the
same conversation request every 250 milliseconds until a matching response
arrives. Pending-capacity exhaustion returns a fixed, low-cardinality
`ServerBusy` rejection without creating a transport or RPC Session; a lost
request or response falls back to bounded retransmission and ultimately a
`TimeoutException`. Explicit rejection surfaces as
`KcpConnectionRejectedException`, and caller cancellation as
`OperationCanceledException`. Transport
rejection does not represent RPC Session admission or Game Session recovery.

One KCP transport connection is identified by remote UDP address, remote UDP
port, and conversation id together. The listener uses that complete identity
for handshake deduplication, KCP datagram routing, failure containment, and
cleanup. Repeated handshakes for the same identity are idempotent. A reused
endpoint with a new conversation id creates a separate RPC
Session within the existing pending and active connection limits; it never
replaces or terminates another conversation. Whether the new RPC Session
recovers an existing Game Session remains a Lakona.Game decision.

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

Services declare their callback contract through attributes. The declaration
shape and association validation are defined in
[Notification Contract Association](source-generation.md#notification-contract-association).

For replayable game notifications above RPC callbacks, publish notification
intent through the Lakona game session APIs. Lakona.Game business handlers do
not receive the connection-scoped callback proxy directly; the game framework
owns reliable push sequencing, acknowledgement, and replay policy.

See [Reliable Push And Resume](../session.md#reliable-push-and-resume) for that
Game-layer delivery contract.

Notification handler exceptions are reported through
`NotificationHandlerException` without disconnecting the transport. Missing
handlers are reported through `UnhandledNotificationReceived`.
Client notification diagnostic observers do not own runtime control flow.
`UnhandledNotificationReceived` and `NotificationHandlerException` invoke
each subscriber independently; a subscriber failure is logged through the
application-owned client logger and cannot stop later subscribers or the
notification consumer.

The client pending-request module consumes ownership of every decoded response.
It transfers a matched response to its waiting caller and immediately disposes
an unmatched response, including one that arrives after caller cancellation,
so pooled payload retention never depends on GC or finalization.

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
