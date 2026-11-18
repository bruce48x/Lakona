# RPC

## Responsibilities and layers

Lakona.Rpc connects server and client code through shared service interfaces,
notification contracts and DTOs. Numeric service, method and notification IDs
identify operations on the wire and remain stable once published. Business
outcomes such as login rejection belong in reply DTOs. Framework outcomes such
as overload or a missing method use `RpcStatus`. See the
[wire protocol](wire-protocol-v1.md) and [status model](status-error-model.md).

`Lakona.Rpc.Analyzers` generates client facades, server binders, notification
binders and method metadata from those contracts. The client runtime owns
pending calls and message dispatch. The server host owns connection admission,
and each accepted Session owns its request processing and connection-scoped
services. See [source generation](source-generation.md) and the
[public API boundaries](public-api-boundaries.md).

### Transport and serializer are replaceable

`ITransport` carries complete frames, `IRpcConnectionAcceptor` supplies incoming
connections, and `IRpcSerializer` encodes and decodes DTOs. A serializer writes
payload bytes synchronously to the supplied `IBufferWriter<byte>`. The runtime
owns that writer throughout the call and completes the envelope. Callers that
need a standalone payload frame can use `SerializeFrame`. Transport
implementers should follow the [transport contract](transport-contract.md).

## Call path and message ordering

A generated client encodes a request with its request ID and submits it to the
connection writer. The caller can await its `ValueTask` while the send is in
progress. The server dispatches the request through its registry and publishes
a response. The client matches the response to the pending call by request ID.
Each connection serializes writes from application and keepalive traffic.
Send completion and queued cancellation follow the
[transport contract](transport-contract.md#responsibility-boundary).

### Server request dispatch

Generated typed binders and raw handlers register with `RpcServiceRegistry`.
Duplicate method IDs within a service are rejected. Both paths share request
admission, response publication, logging and frame ownership. Registrations
handle typed payloads or supply a raw codec for framework control protocols.

Each Session admits request starts in FIFO order. Admission gates run in that
order and fail closed. Once a service method yields, another admitted request
can start within the concurrency budget. Independent requests can complete and
respond in a different order. Gate rejections, missing routes and handler
results use the same response path. Handler errors are translated before
sending, so a failed response write ends the Session without a second reply.
See [error mapping](status-error-model.md#implementation-mapping) and
[request telemetry](../observability.md#instrumentation-scopes).

### Ordered message entry

On the client, responses and pushes enter one FIFO. The dispatcher starts one
synchronous segment at a time. A synchronous handler holds up later entries.
Once a handler yields, the next message can enter, so a push handler can await
another RPC on the same connection. A post racing the serial dispatcher's
transition to idle must still be processed, with only one consumer active.

The runtime binds the synchronization context at startup or supplies a serial
context. Generated Game clients retain the binding across reconnects. For an
already-awaited native RPC on that context, push entry and the caller's first
synchronous continuation after the response follow wire order, for example
`P1, P2, R, P3, P4`. A deferred await, conversion to Task, async wrapper or
context switch puts the continuation outside that guarantee. Cancellation,
timeout and disconnect can complete a call independently of incoming frames.
Duplicate and orphan responses never invoke user code.

### Client notification queue

A slow notification handler can delay dispatch while the receive loop continues
accepting responses and pushes. The client keeps queued notifications and
allows the queue to grow. It warns at increasing high-water marks for message
count or retained wire bytes, starting at 256 notifications or 1 MiB.
See the [queue memory and latency tradeoff](../performance.md#client-notification-queue).

### Callback is part of the contract

Each service declares its server-to-client push contract through a
[notification contract association](source-generation.md#notification-contract-association).
Missing handlers raise `UnhandledNotificationReceived`. Handler failures raise
`NotificationHandlerException` and leave the connection active. Each diagnostic
subscriber runs independently. The client logs subscriber failures and
continues delivery.

Game owns reliable push sequencing, acknowledgement and replay above RPC.
Game handlers publish notification intent through Game session APIs. A
response may need to wait for earlier notification publications before entering
the connection writer. See the [publication barrier](../session.md#notification-and-response-publication-order)
and [reliable push and resume](../session.md#reliable-push-and-resume).

## Frame ownership

| Boundary | Ownership rule |
| --- | --- |
| Encoding | The runtime reserves the envelope header and the serializer writes into the final frame. Raw handlers can also write directly into an envelope-owned buffer. |
| Sending | The sender keeps frame memory valid until the transport operation completes, including after cancellation is requested. See the [transport responsibility boundary](transport-contract.md#responsibility-boundary). |
| Decoding | Decoders borrow the input and validate the whole envelope before retaining payload or metadata slices. Rejection leaves the input with its caller and retains no new lease. |
| Slicing | Each non-empty `TransportFrame` handle owns one lease. A slice can outlive its parent. Disposing a handle releases it once, and further access through it throws `ObjectDisposedException`. Other handles remain valid. `TransportFrame.Empty` owns no buffer and is reusable. |
| Publishing a response | The dispatcher owns the encoded response while notification publications drain and through the send. It releases the frame on success, cancellation or failure at either step. |
| Receiving a response | The pending-call owner transfers a matched response to its caller and immediately disposes an unmatched or late response. |

Frame size limits apply before a complete payload is available. A length prefix
alone must not trigger a payload-sized allocation. See [resource limits](wire-protocol-v1.md#resource-limits)
and the [performance guidance](../performance.md).

## Client lifetime

`RpcClientRuntime.ConnectionState` exposes `Created`, `Connecting`, `Connected`,
`Stopped` and `Disposed`. `Connected` means transport initialization completed
and RPC loops started. Game handshake and login complete separately. A failed
or canceled startup reaches `Stopped` and may be retried if the transport
supports it. After an established connection ends, reconnect with a fresh
runtime and transport.

`ConnectionStateChanged` reports previous and current state once per transition,
in order on the thread pool. Subscribe before startup and read the property
for initial state. Delivery captures subscribers at transition time, runs
outside runtime locks and isolates subscriber exceptions. The property can
advance before a callback runs. UI subscribers marshal to their main thread.
The generated client forwards these changes. Game uses the stopped state for
early visibility and the terminal event to start disconnect recovery.
See [Game client lifecycle](../session.md#game-client-lifecycle).

- **Connection termination.** The client publishes `Stopped` when it closes
  send admission. It cancels admitted writes, joins receive and keepalive,
  dispatches already-received messages in order, and fails remaining calls.
  Admitted sends, including those using the default token, finish and release
  their frames before `Disconnected` or transport disposal. The first terminal
  I/O failure remains the disconnect reason. `Disconnected` has no ordering
  guarantee relative to asynchronous state-change callbacks.
- **Caller cancellation.** Canceling one call ends that caller's wait. Work
  accepted by the peer may still complete. A transport may treat cancellation
  during an active write as terminal.
- **Explicit disposal.** The client publishes `Disposed` immediately, closes
  intake and dispatch, cancels pending calls, and joins startup and active I/O.
  Late startup completion cannot start loops or restore a live state. A running
  business handler retains its frame until it exits and can itself await
  disposal. Queued callbacks are canceled. Await `DisposeAsync` asynchronously
  on engine threads. Concurrent disposals share the same result. A throwing
  cancellation callback still permits cleanup of every owned resource, and
  disposal reports cancellation and cleanup failures together.

## Host and Session lifetime

### Connection and request admission

The application maps process signals to the cancellation token passed to
`RpcServerHost.RunAsync`. The bounded acceptor owns each accepted connection
until handoff. The host reserves an active slot before constructing a Session
and closes new connections when capacity is exhausted. Session admission gates
can supply lifetime tokens and leases. A rejected connection releases its
transport and acquired leases without a Session lifecycle notification.

An admitted Session takes ownership of its transport. Game handshake policy
runs above this admission boundary, including for KCP connections. Within the
Session, active and queued requests share a finite budget. When it is full,
the receive loop waits for the `Overloaded` response before reading again.

### Session completion

The Session owns its receive loop, keepalive work and in-flight requests.
Connection-scoped service activation publishes one instance per registration.
The Session releases factory-created services after their requests finish.
Explicitly bound singleton instances remain caller-owned.

- **Peer disconnect.** EOF or a terminal receive error cancels Session work.
  Even if a cancellation callback throws, the Session drains requests before
  releasing scoped services. A prior I/O error remains the disconnect reason
  and an additional callback failure is logged. On EOF alone, a failing
  callback supplies the terminal error.
- **Response write failure.** The Session records the write error and aborts
  its owned transport so pending I/O can end. The handler may already have
  applied its business changes. The application decides whether to retry.
- **Keepalive.** On either endpoint, the probe deadline starts before the Ping
  enters the shared writer. Any inbound frame ends the probe. At most one Ping
  write is queued or active. A later period of silence gets a new deadline even
  if that write is still pending. Timeout records the reason and cancels I/O.
  The keepalive loop joins its send before releasing the frame, and the Session
  joins keepalive before releasing scoped state.

The host returns admission leases and the active slot after Session cleanup.
The host's terminal observation point is `OnSessionDisconnectedAsync`: its
contract places it after Session work, transport release and admission release.

### Host shutdown

The listener reports an unexpected accept failure and its original exception
through `OnListenerFailed` before disposal and Session drain. The first accept
can fail before `OnListeningAsync`. The host still owns buffered connections
and Session cleanup.

1. The host stops admission and disposes the acceptor. It then starts a
   shared Session shutdown deadline, configured by `UseShutdownTimeout`
   (default 15 seconds). It cancels Sessions asynchronously and waits for both
   cancellation callbacks and connection tasks, even if every Session has left
   the active set.
2. If the deadline expires, the host aborts active transports to wake I/O.
   Waiting for those aborts has a separate window of the same duration.
   Shutdown reports `RpcServerShutdownTimeoutException`. When a listener
   failure already explains shutdown, the host preserves that failure and
   logs the additional cleanup error.
3. The host continues observing unfinished callbacks and cleanup after a
   timeout. A handler that ignores cancellation keeps its scoped service until
   it exits. Late completion follows normal resource ownership. The host
   disposes its Session cancellation source after callbacks and connection
   tasks finish. Callback failures surface after cleanup or are logged when
   preserving an earlier host error. The application owns the final process
   termination policy.
