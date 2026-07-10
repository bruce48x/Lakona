# Node-Directed Actor Replies Design

## Status

Approved for implementation.

## Problem

Lakona currently mixes two transport models for framework request/reply traffic.
Remote actor requests and actor-host creation requests can be sent directly to
a selected node through `IClusterNodeSender`, while replies are sent through
`IClusterRouter` using a synthetic `reply/<source-node>` route.

The normal multi-node host lifecycle does not register or renew those reply
routes. The Agar three-node topology therefore reaches `data-1`, but an
actor-host creation request cannot complete its response path reliably. The
gateway surfaces the resulting framework failure as an RPC `HandlerError`.
Low-level tests currently hide the defect by using routers that accept every
message without exercising a real node directory and handler chain.

## Goal

Make framework request/reply transport symmetric and reliable: when a request
contains a source node and correlation id, the target sends its reply directly
to that node. Business actor routes remain route-directory owned; reply
delivery does not use `IRouteDirectory`.

## Non-Goals

- This change does not introduce Startup Actor service groups.
- It does not add automatic retries for ambiguous timeouts.
- It does not change client-facing RPC framing or status codes.
- It does not add durable or replicated pending-request state.

## Chosen Architecture

### Request path

Keyed actor calls that do not yet know an owner continue to use
`IClusterRouter` and the actor route directory. Calls that already selected a
node, including actor-host creation and generated remote actor invocation,
continue to use `IClusterNodeSender`.

### Reply path

All actor-framework replies use `IClusterNodeSender`:

```txt
target handler
  -> destination SourceNode
  -> route key reply/<SourceNode>
  -> node directory resolves the destination node's cluster endpoint
  -> ClusterMessage reaches the destination's RemoteActorGateway reply handler
  -> CorrelationId completes the pending request
```

The reply route key remains a local handler-dispatch key carried in the
message. It is no longer a globally registered route-directory entry.

`RemoteActorGateway.SendReplyAsync` changes from router-based delivery to a
node-directed API that receives:

- `IClusterNodeSender`
- the replying node id
- the destination/source node id
- correlation id
- payload
- cancellation token

It returns the actual `ClusterSendStatus`. Callers must propagate reply-send
failure instead of returning `Accepted` after silently dropping the reply.

### Pending request ownership

The caller registers a pending correlation before sending the request. If the
request send is rejected, throws, or is cancelled, the caller removes the
pending registration. A successful send waits for the reply, cancellation, or
timeout. No reply route is created or removed per request.

### Source identity

Reply messages record the actual replying node as `SourceNode`; they do not
reuse the destination node as the source. This keeps diagnostics and future
authorization checks accurate.

## API Changes

- `RemoteActorGateway.SendReplyAsync` changes to node-directed delivery and
  returns `ClusterSendStatus`.
- `ActorRuntimeRemoteExtensions.AskRemoteAsync` removes its
  `IRouteDirectory` parameter and the temporary reply-route registration.
- `HotfixActorClusterHandler` receives `IClusterNodeSender` and local-node
  identity for replies.
- Framework construction sites and tests move from reply routers to the
  node-directed reply sender.

These are intentionally breaking changes to low-level framework APIs. Lakona
is pre-1.0 and the repository policy prefers a coherent runtime model over
preserving a defective escape hatch.

## Failure Semantics

- Request rejected before handler acceptance: cancel pending correlation and
  return the mapped structured failure immediately.
- Reply delivery rejected: target handler returns the reply-send status to the
  original cluster request instead of reporting `Accepted`.
- Timeout after request acceptance: return timeout; do not resend because the
  behavior may already have executed.
- Caller cancellation: cancel the pending correlation and do not reinterpret
  it as node failure.
- Late reply after timeout/cancellation: reply handler accepts and discards it
  because no pending correlation remains.

## Diagnostics

Existing cluster status metrics remain the source of low-cardinality failure
diagnostics. Logs may include operation kind and node id, but must not include
actor ids, correlation ids, request payloads, session ids, or user ids.

## Tests

Focused tests must use real in-memory node infrastructure rather than a router
that always accepts:

1. Actor-host create travels from node A to node B and returns a successful
   reply through node-directed delivery.
2. A normal remote actor call returns its serialized reply through the same
   path.
3. Missing destination node fails before execution and clears pending state.
4. Reply delivery failure is propagated instead of silently accepted.
5. Cancellation and timeout remove pending state; a late reply is harmless.
6. Existing local dispatch, tell, serialization, and handler ordering tests
   remain green.

Integration acceptance is the Agar three-node Docker plus Unity PlayMode smoke
test. Guest login must create `UserActor` on `data-1` without `RouteNotFound`.

## Documentation

`docs/cluster.md` and `docs/actor.md` will distinguish:

- business routes resolved through `IRouteDirectory`;
- node-directed framework control messages resolved through `INodeDirectory`;
- reply correlation as destination-local dispatch state rather than a cluster
  route.

## Versioning

Every modified package under `src/**` is bumped by one minor version with patch
reset to zero. Dependency-closure packages and Lakona.Tool release constants
are updated according to the repository package-version graph.

## Rollout

This design is implemented and validated as an independent milestone before
Startup Actor service groups. The later Startup design depends on this reply
path but does not need to be included in the same runtime commit.
