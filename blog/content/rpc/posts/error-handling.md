+++
title = "Error Handling"
date = 2026-05-12T09:20:00+08:00
+++

This page describes the error semantics currently implemented by the Lakona.Rpc runtime. It is not a guide to business error-code design; the business layer should still express expected failures in its own DTOs, such as login failure, insufficient inventory space, or a missing room.

## Protocol Status

RPC response envelopes use `RpcStatus` for framework-level results:

- `Ok = 0`: the service method returned successfully. The payload is the return DTO; `void` returns use an empty payload.
- `NotFound = 1`: the server could not find a handler for the requested `serviceId:methodId`.
- `HandlerError = 2`: the server handler failed or returned an invalid framework response.
- `Overloaded = 3`: the server could not accept the request because it is overloaded, such as when a request queue is full.
- `BadRequest = 4`: the request reached the RPC layer but was invalid for the target RPC contract.
- `ProtocolError = 5`: the peer violated the wire protocol or connection state machine.

These statuses only cover the framework layer. Do not map recoverable business failures to server exceptions; returning a business DTO such as `LoginReply { Success, ErrorCode, Message }` is more stable.

## Server Exception Propagation

The current server does not send raw exception types, stacks, or internal messages directly to clients. `ServerRequestDispatcher` records server logs and returns:

```text
RpcStatus.HandlerError
ErrorMessage = "RPC handler failed."
```

When a handler is missing, the error message includes the missing `serviceId:methodId`. When the request queue is full, the status is `Overloaded` and the message indicates server overload.

This means clients cannot branch on server exception types. Failures that clients need to understand must be represented in return DTOs.

## Client Failure Modes

Generated clients ultimately call `RpcClientRuntime.CallAsync`. Current behavior:

- A non-`Ok` response throws `RpcException`, with `Status`, `ErrorMessage`, `RequestId`, `ServiceId`, and `MethodId`.
- If the request's `CancellationToken` is canceled, the pending request is canceled.
- Disconnects, transport close, keepalive timeout, and similar failures end the receive loop and fail pending requests with the disconnect reason.
- Disposing the client causes pending requests to receive `ObjectDisposedException`.

Client code should handle "RPC call failed" separately from "business result failed". The former usually means a connection, protocol, server handler, or deployment issue; the latter is part of normal game flow.

## Recommended Practices

Define expected errors in business DTOs instead of using exceptions for normal branches.

Pass a reasonable `CancellationToken` to every user-triggered RPC, such as cancellation when a UI closes, a scene changes, or an object is destroyed.

Listen to `RpcClientRuntime.Disconnected`, or to the dispose / reconnect flow exposed by the generated client, and let the application layer decide whether to create a new client and reconnect.

Record business context inside server handlers, but do not put sensitive information into return DTOs or thrown exception messages.

## Not Implemented Today

The current repository does not include a shared business error-code protocol, automatic retry policy, server exception type to client type mapping, distributed tracing integration, or per-method timeout configuration. Add these in the application layer when needed.
