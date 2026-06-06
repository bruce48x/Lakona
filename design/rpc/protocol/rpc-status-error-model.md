# RPC Status Error Model

Date: 2026-06-04

## Decision

`RpcStatus` represents framework and infrastructure outcomes only. Business failures stay in business DTOs.

The stable baseline status set is:

```csharp
public enum RpcStatus : byte
{
    Ok = 0,
    NotFound = 1,
    HandlerError = 2,
    Overloaded = 3,
    BadRequest = 4,
    ProtocolError = 5
}
```

The old public `Exception` status name is removed. `HandlerError` is narrower and avoids confusing RPC framework status with .NET exception transport.

## Rationale

Long-lived client code needs machine-readable framework failure categories for retry policy, observability, and deployment diagnosis. A single `Exception` status grouped handler failures, invalid handler responses, and server overload together, which made those categories harder to reason about.

Application outcomes such as login failure, insufficient inventory space, room not found, cooldown not ready, and rejected matchmaking are expected business states. They should be modeled in normal response DTO fields, not in `RpcStatus`.

## Status Semantics

`Ok`
: The service method completed successfully. The payload contains the serialized return DTO. `void` returns use an empty payload.

`NotFound`
: No handler was found for the requested `serviceId:methodId`. Clients should usually not retry. This usually indicates client/server version mismatch, deployment drift, or missing service registration.

`HandlerError`
: The server-side handler failed while executing, or returned an invalid framework response such as null. The server logs the full exception. The client receives a stable sanitized message through `RpcException`.

`Overloaded`
: The server cannot currently accept the request, such as when a session request queue is full. Clients may apply application-owned backoff or retry only when the operation is safe to retry. The framework must not automatically retry RPC calls because it cannot know method idempotency.

`BadRequest`
: The frame reached the RPC request layer, but the request content is invalid for the RPC contract. Examples include malformed request payload, deserialization failure, or request data that cannot be interpreted as the generated DTO shape. Clients should usually not retry unchanged data.

`ProtocolError`
: The peer violated the wire protocol or connection state machine. Examples include unknown frame type, invalid envelope shape, or a frame that is illegal in the current state. This usually should close the connection rather than return a normal response. Use this response status only when there is a clear request id to answer.

## Implementation Mapping

- Missing handler returns `NotFound`.
- Handler exception returns `HandlerError`.
- Handler returns null response returns `HandlerError`.
- Session request queue full returns `Overloaded`.
- Non-`Ok` responses cause the client runtime to throw `RpcException`.

`BadRequest` and `ProtocolError` reserve stable protocol surface for validation improvements. Call sites should use them when there is a clear existing mapping; broad parser or deserializer rewrites are separate work.

## Client Failure Taxonomy

Client code should treat these channels separately:

- `RpcException`: remote RPC framework failure, classified by `RpcException.Status`.
- `OperationCanceledException`: local caller cancellation or timeout policy.
- `ObjectDisposedException`, transport exceptions, and `Disconnected`: connection lifecycle failure.
- Business response DTOs: normal expected business failures.

Generated clients continue returning successful business DTOs and throwing `RpcException` for non-`Ok` framework responses. They should not introduce a framework result union for every RPC call.

## Observability

The server should log handler exceptions with request id, service id, method id, and session context id.

For `Overloaded`, the response status should be machine-readable. The error message may stay short and safe, for example:

```text
RPC server is overloaded; request queue is full.
```

Client observability should rely on `RpcException.Status`, `RequestId`, `ServiceId`, and `MethodId`, not string matching against error messages.

## Non-Goals

This design does not add:

- A shared business error-code protocol.
- Automatic retry policy.
- Server exception type serialization.
- Stack trace transport to clients.
- Per-method timeout configuration.
- Authentication or authorization status codes.

Those can be handled by application DTOs or later framework features if they become unavoidable. They should not be added speculatively to `RpcStatus`.

## Compatibility Position

Because the project is still early, there is no `Exception` alias for compatibility. Removing it avoids long-term ambiguity.

Existing docs, tests, and generated references should use `HandlerError` and `Overloaded`.
