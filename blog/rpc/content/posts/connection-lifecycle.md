+++
title = "Connection Lifecycle"
date = 2026-05-12T09:10:00+08:00
+++

Lakona.Rpc splits connection lifetime into two layers: the low-level `ITransport` owns connection and frame I/O, while `RpcClientRuntime` / `RpcSession` own RPC request, response, notification, keepalive, and shutdown behavior.

## Client Lifecycle

The generated `RpcClient` facade holds an internal `RpcClientRuntime`. When `ConnectAsync` is called, the runtime:

1. Calls the transport's `ConnectAsync`.
2. Initializes keepalive state.
3. Starts the receive loop.
4. Starts the notification loop.
5. Starts the keepalive loop if keepalive is enabled.

The same runtime can only be started once; repeated starts throw `InvalidOperationException`. After disconnecting or disposing, the same runtime must not be reused. The application layer should dispose the current client, create a new transport, serializer, options object, and generated client, then reconnect.

## Server Lifecycle

`RpcServerHost` accepts connections through `IRpcConnectionAcceptor`. Each accepted connection creates an `RpcSession`. The session:

- Calls `ConnectAsync` on the accepted transport.
- Starts the receive loop.
- Creates or reuses session-scoped services per request.
- Cleans up scoped services when the session stops.
- Disposes the transport when the session is disposed if `ownsTransport: true`.

`RpcServerHostBuilder.UseKeepAlive(...)` passes keepalive settings to every new session.

`RpcSession` is also a single-connection object. `StopAsync` requests shutdown for the current session, waits for in-flight requests to complete, and leaves the session in a terminal state. Starting the same session again after `StopAsync`, remote disconnect, keepalive timeout, or `DisposeAsync` is not supported. A new accepted connection must get a new `RpcSession`.

## Shutdown and Disconnects

Client `DisposeAsync` cancels internal loops, fails pending requests, closes the notification queue, and disposes the transport.

Server `StopAsync` cancels session loops, waits for in-flight requests to complete, and then cleans up session state. `StopAsync` and `DisposeAsync` are idempotent cleanup operations, but idempotent cleanup does not mean the object can be restarted. When the host is canceled, it stops the accept loop and waits for tracked connection tasks to finish.

The corresponding loop ends when a low-level receive returns an empty frame, when the transport throws a disconnect-related exception, or when keepalive times out. Both client and server expose a `Disconnected` event with an available disconnect reason; normal local shutdown may report `null`. A successfully started runtime or session raises at most one `Disconnected` event for that lifetime.

## Transport State

`ITransport.IsConnected` is a low-level diagnostic signal. It reflects the transport's best-known local state at the moment it is read; it is not a synchronization primitive and does not guarantee that the next send or receive will succeed.

Application code should not use `IsConnected` as a pre-send correctness check. Send the RPC call, await the result, and handle success, cancellation, RPC errors, transport failures, and `Disconnected` events. UI state should come from the application connection state machine, not from polling `IsConnected`.

## Keepalive

Keepalive is disabled by default. `RpcKeepAliveOptions` includes:

- `Enabled`
- `Interval`: how long to wait without receiving any frame before sending a ping, default 15 seconds.
- `Timeout`: how long to wait after a ping without receiving any inbound frame before disconnecting, default 45 seconds.
- `MeasureRtt`: whether to record ping / pong RTT.

Only inbound traffic proves the peer is still alive; locally sent frames do not suppress probes. After a client keepalive timeout, `TimedOutByKeepAlive` becomes true.

## Reconnect Ownership

Lakona.Rpc currently does not include automatic reconnect. Reconnects usually involve login state, room state, scene state, request replay, UI prompts, and idempotency, all of which belong to the application layer.

Recommended application behavior:

- Use one connection state machine: `Idle`, `Connecting`, `Connected`, `Disconnecting`, `Disconnected`.
- Create a new transport and generated client for every reconnect.
- Give unfinished user operations explicit UI state.
- Restore room, matchmaking, or battle state only after login or authentication succeeds.

## Not Implemented Today

The current repository does not include automatic reconnect, request replay, session resume, offline queues, adaptive heartbeat tuning, or server session migration across connections.
