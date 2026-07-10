# Node-Directed Actor Replies Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver actor-framework replies directly to the request source node so actor-host creation and remote actor calls work in a real multi-node topology without synthetic reply-route registration.

**Architecture:** Business actor requests that need ownership lookup continue through `IClusterRouter`. Replies use `IClusterNodeSender`, with the destination node supplied by the request envelope and the actual replying node recorded as the message source. Pending correlations are registered before send and removed on rejection, exception, cancellation, or timeout; a late reply is accepted and discarded.

**Tech Stack:** .NET 10, C# 13, xUnit v3, Lakona cluster node directory/messenger abstractions, Roslyn incremental source generation, PowerShell 7 validation scripts.

---

## Scope and risk checkpoint

- **Classification:** Large cross-cutting change because it breaks low-level public APIs, changes distributed request/reply behavior, changes generated code, and requires multi-node acceptance testing.
- **Affected packages:** `Lakona.Game.Server` and `Lakona.Game.Server.Generators`; the package graph also requires a `Lakona.Tool` release-anchor bump.
- **Affected tests/docs:** server actor tests, generator snapshot assertions, `docs/actor.md`, `docs/cluster.md`, and Agar three-node E2E.
- **Continuity owner:** One implementation owner must keep gateway pending state, handler reply status, generated handler construction, and real-node integration tests coherent.
- **Safe independent review slices:** Documentation wording, package-version graph verification, and final diff review only after runtime tests pass.
- **Compatibility:** Intentionally breaking. Remove the router-based `SendReplyAsync` overload and the `IRouteDirectory` parameter from `AskRemoteAsync`; do not add shims.
- **Primary risks:** silently dropping a reply-send failure, leaking pending correlations on pre-acceptance failure, recording the destination as reply source, or retaining tests that manually register `reply/<node>`.

## File map

### Runtime and generator

- Modify `src/Lakona.Game.Server/Actors/RemoteActorGateway.cs`: node-directed reply API and pending-state cleanup contract.
- Modify `src/Lakona.Game.Server/Actors/ActorRuntimeRemoteExtensions.cs`: remove temporary reply-route registration and clean pending state on failed sends.
- Modify `src/Lakona.Game.Server/Actors/HotfixActorClusterHandler.cs`: inject node sender/local identity and propagate reply delivery status.
- Modify `src/Lakona.Game.Server.Generators/TypedActorGenerator.cs`: generated handlers use node sender/local identity and return reply-send status.

### Tests

- Modify `tests/Lakona.Game.Server.Tests/RemoteActorGatewayTests.cs`: real two-node request/reply path without a reply route, missing-node cleanup, timeout/cancellation, and late replies.
- Modify `tests/Lakona.Game.Server.Tests/HotfixActorClusterHandlerTests.cs`: actor-host create and behavior replies target the source node and propagate failed delivery.
- Modify `tests/Lakona.Game.Server.Tests/TypedActorDispatcherTests.cs`: generated/manual typed dispatch reply status and source identity.
- Modify `tests/Lakona.Game.Server.Generators.Tests/TypedActorGeneratorTests.cs`: generated constructor and reply-call shape.
- Modify `tests/Lakona.Game.Server.Tests/Hosting/LakonaClusterEndpointServiceCollectionExtensionsTests.cs`: DI construction still resolves the changed handler dependencies.

### Documentation and versions

- Modify `docs/actor.md`: business route versus destination-local reply dispatch.
- Modify `docs/cluster.md`: `IRouteDirectory` versus `INodeDirectory` responsibility.
- Modify `src/Lakona.Game.Server/Lakona.Game.Server.csproj`: `0.11.0` to `0.12.0`.
- Modify `src/Lakona.Game.Server.Generators/Lakona.Game.Server.Generators.csproj`: `0.2.0` to `0.3.0`.
- Modify `src/Lakona.Tool/Lakona.Tool.csproj`: `0.17.0` to `0.18.0` as release anchor.

## Task 1: Replace the fake reply-route test with a real node-directed red test

**Files:**

- Modify: `tests/Lakona.Game.Server.Tests/RemoteActorGatewayTests.cs`

- [ ] **Step 1: Rewrite the successful ask test around node infrastructure**

Use one `InMemoryNodeDirectory`, one `InMemoryLoopbackNodeMessenger`, and two `ClusterNodeSender` instances. Register node A and node B in the node directory, register only the actor business route in `InMemoryRouteDirectory`, and do not register `ClusterActorRouteKeys.ForReply(nodeA)`.

The target handler must use the planned API shape:

```csharp
var status = await RemoteActorGateway.SendReplyAsync(
    nodeSenderB,
    replyingNode: new NodeId("node-b"),
    destinationNode: envelope.SourceNode,
    envelope.ReplyCorrelationId!,
    envelope.Payload,
    cancellationToken);
return status;
```

Assert the request completes, the reply handler on node A receives `_actor_reply`, `SourceNode` is `node-b`, and no reply route exists in `IRouteDirectory`.

- [ ] **Step 2: Run the focused test and verify it fails for the missing API**

Run:

```powershell
dotnet test tests/Lakona.Game.Server.Tests/Lakona.Game.Server.Tests.csproj --no-restore --filter "FullyQualifiedName~RemoteActorGatewayTests.AskRemoteAsync_sends_request_and_receives_node_directed_reply"
```

Expected: FAIL to compile because `SendReplyAsync` still accepts `IClusterRouter`, and `AskRemoteAsync` still requires `IRouteDirectory`.

- [ ] **Step 3: Add rejection and late-reply red tests**

Add tests with these exact contracts:

```csharp
[Fact]
public async Task AskRemoteAsync_failed_request_send_removes_pending_registration()
```

After the request returns a non-`Accepted` send status, assert
`gateway.PendingCount == 0`; the send path already removed its private,
randomly generated correlation.

```csharp
[Fact]
public async Task Reply_handler_accepts_late_reply_after_timeout_without_recreating_pending_state()
```

Let a 20 ms pending request time out, deliver a matching reply afterward, assert the handler returns `Accepted`, then assert `TryCancelPending` is `false`.

- [ ] **Step 4: Commit the red tests**

```powershell
git add tests/Lakona.Game.Server.Tests/RemoteActorGatewayTests.cs
git commit -m "Test node-directed actor replies"
```

## Task 2: Implement the node-directed gateway contract

**Files:**

- Modify: `src/Lakona.Game.Server/Actors/RemoteActorGateway.cs`
- Test: `tests/Lakona.Game.Server.Tests/RemoteActorGatewayTests.cs`

- [ ] **Step 1: Replace `SendReplyAsync` with the direct sender signature**

Implement this public surface and delete the router-based overload:

```csharp
public static ValueTask<ClusterSendStatus> SendReplyAsync(
    IClusterNodeSender nodeSender,
    NodeId replyingNode,
    NodeId destinationNode,
    string correlationId,
    ReadOnlyMemory<byte> payload,
    CancellationToken cancellationToken)
{
    ArgumentNullException.ThrowIfNull(nodeSender);
    ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);

    var route = ClusterActorRouteKeys.ForReply(destinationNode);
    var reply = new ClusterMessage(
        route,
        ReplyKind,
        payload,
        DateTimeOffset.UtcNow.AddSeconds(30),
        replyingNode,
        correlationId);

    return nodeSender.SendAsync(destinationNode, route, reply, cancellationToken);
}
```

Do not register, renew, or remove any route-directory entry.

Add `internal int PendingCount => _pending.Count;` for focused leak assertions;
do not expose correlations or a mutable pending collection.

- [ ] **Step 2: Keep late replies idempotent**

Keep `ReplyHandler.HandleAsync` returning `Accepted` when the correlation is absent. It must remove and complete an existing pending item exactly once and never create state from a reply.

- [ ] **Step 3: Run gateway tests**

```powershell
dotnet test tests/Lakona.Game.Server.Tests/Lakona.Game.Server.Tests.csproj --no-restore --filter "FullyQualifiedName~RemoteActorGatewayTests"
```

Expected: the gateway tests compile; node-directed reply, timeout, and late-reply tests PASS. The `AskRemoteAsync` test may remain red until Task 3.

- [ ] **Step 4: Commit**

```powershell
git add src/Lakona.Game.Server/Actors/RemoteActorGateway.cs tests/Lakona.Game.Server.Tests/RemoteActorGatewayTests.cs
git commit -m "Send actor replies directly to source nodes"
```

## Task 3: Remove temporary reply routes from routed actor asks

**Files:**

- Modify: `src/Lakona.Game.Server/Actors/ActorRuntimeRemoteExtensions.cs`
- Modify: `tests/Lakona.Game.Server.Tests/RemoteActorGatewayTests.cs`

- [ ] **Step 1: Lock the new `AskRemoteAsync` signature in the test**

The invocation must be:

```csharp
var result = await runtimeA.AskRemoteAsync(
    routerA,
    gatewayA,
    new NodeId("node-a"),
    "echo/1",
    EchoKind,
    () => EchoPayload,
    static reply => reply,
    TimeSpan.FromSeconds(5),
    cancellationToken);
```

There is no `IRouteDirectory` argument.

- [ ] **Step 2: Delete placeholder endpoint and reply registration**

Remove `PlaceholderEndpoint`, `IRouteDirectory routeDirectory`, `RouteLocation`, and the `RegisterAsync` call. Register the pending correlation immediately before `router.SendAsync`.

- [ ] **Step 3: Cancel pending state on every pre-acceptance exit**

Use this control shape:

```csharp
var pending = gateway.RegisterPendingAsync(correlationId, timeout, cancellationToken);
ClusterSendStatus status;
try
{
    status = await router.SendAsync(envelope.ToClusterMessage(), cancellationToken)
        .ConfigureAwait(false);
}
catch
{
    gateway.TryCancelPending(correlationId);
    throw;
}

if (status != ClusterSendStatus.Accepted)
{
    gateway.TryCancelPending(
        correlationId,
        new InvalidOperationException($"Remote actor call failed with status: {status}."));
    throw new InvalidOperationException(
        $"Remote actor call failed with status: {status}. ActorId={actorId}, Kind={kind}");
}

return deserializeResult(await pending.ConfigureAwait(false));
```

Do not retry a timeout after `Accepted`.

- [ ] **Step 4: Run focused tests**

```powershell
dotnet test tests/Lakona.Game.Server.Tests/Lakona.Game.Server.Tests.csproj --no-restore --filter "FullyQualifiedName~RemoteActorGatewayTests|FullyQualifiedName~RemoteActorInvokerTests"
```

Expected: PASS, including pending cleanup on rejection/cancellation and harmless late reply.

- [ ] **Step 5: Commit**

```powershell
git add src/Lakona.Game.Server/Actors/ActorRuntimeRemoteExtensions.cs tests/Lakona.Game.Server.Tests/RemoteActorGatewayTests.cs
git commit -m "Remove synthetic actor reply routes"
```

## Task 4: Make hotfix and actor-host handlers propagate reply delivery

**Files:**

- Modify: `src/Lakona.Game.Server/Actors/HotfixActorClusterHandler.cs`
- Modify: `tests/Lakona.Game.Server.Tests/HotfixActorClusterHandlerTests.cs`
- Modify: `tests/Lakona.Game.Server.Tests/Hosting/LakonaClusterEndpointServiceCollectionExtensionsTests.cs`

- [ ] **Step 1: Write handler red tests**

Replace `RecordingClusterRouter` with an `IClusterNodeSender` fake that records destination, route, and message. Add:

```csharp
[Fact]
public async Task HandleAsync_actor_host_create_replies_directly_to_source_node()
```

Assert destination `source-node`, reply route `reply/source-node`, message source `local`, matching correlation, and returned `Accepted`.

Add:

```csharp
[Fact]
public async Task HandleAsync_returns_reply_delivery_failure_after_behavior_executes()
```

Configure the sender to return `ClusterSendStatus.Failed`; assert the behavior ran exactly once and the handler returns `Failed` rather than `Accepted`.

- [ ] **Step 2: Run the red tests**

```powershell
dotnet test tests/Lakona.Game.Server.Tests/Lakona.Game.Server.Tests.csproj --no-restore --filter "FullyQualifiedName~HotfixActorClusterHandlerTests"
```

Expected: FAIL because the handler still requires `IClusterRouter` and ignores the reply status.

- [ ] **Step 3: Change handler dependencies**

Use fields and constructor parameters:

```csharp
private readonly IClusterNodeSender _nodeSender;
private readonly LocalActorNodeIdentity _localNode;

public HotfixActorClusterHandler(
    IActorRuntime runtime,
    IRemoteActorSerializer serializer,
    IClusterNodeSender nodeSender,
    LocalActorNodeIdentity localNode,
    IServiceProvider services)
```

Every reply becomes:

```csharp
return await RemoteActorGateway.SendReplyAsync(
    _nodeSender,
    _localNode.NodeId,
    envelope.SourceNode,
    envelope.ReplyCorrelationId,
    replyPayload,
    cancellationToken).ConfigureAwait(false);
```

For messages without a reply correlation, preserve the existing dispatch status. For actor-host creation use `message.SourceNode` and `message.CorrelationId` in the same way.

- [ ] **Step 4: Verify DI construction**

Update direct constructor calls in tests. Keep production registration as `IClusterMessageHandler, HotfixActorClusterHandler`; the cluster endpoint service collection must already provide `IClusterNodeSender` and `LocalActorNodeIdentity`. Assert provider resolution succeeds.

- [ ] **Step 5: Run server tests**

```powershell
dotnet test tests/Lakona.Game.Server.Tests/Lakona.Game.Server.Tests.csproj --no-restore --filter "FullyQualifiedName~HotfixActorClusterHandlerTests|FullyQualifiedName~LakonaClusterEndpointServiceCollectionExtensionsTests"
```

Expected: PASS.

- [ ] **Step 6: Commit**

```powershell
git add src/Lakona.Game.Server/Actors/HotfixActorClusterHandler.cs tests/Lakona.Game.Server.Tests/HotfixActorClusterHandlerTests.cs tests/Lakona.Game.Server.Tests/Hosting/LakonaClusterEndpointServiceCollectionExtensionsTests.cs
git commit -m "Propagate actor reply delivery status"
```

## Task 5: Update generated typed actor handlers

**Files:**

- Modify: `src/Lakona.Game.Server.Generators/TypedActorGenerator.cs`
- Modify: `tests/Lakona.Game.Server.Generators.Tests/TypedActorGeneratorTests.cs`
- Modify: `tests/Lakona.Game.Server.Tests/TypedActorDispatcherTests.cs`

- [ ] **Step 1: Add generated-source red assertions**

Assert generated handlers contain:

```csharp
private readonly global::Lakona.Game.Cluster.IClusterNodeSender _nodeSender;
private readonly global::Lakona.Game.Server.Actors.LocalActorNodeIdentity _localNode;
```

and do not contain `IClusterRouter _router`. Assert the reply call passes `_nodeSender`, `_localNode.NodeId`, and `envelope.SourceNode`, and the case returns that send status.

- [ ] **Step 2: Run generator tests and verify failure**

```powershell
dotnet test tests/Lakona.Game.Server.Generators.Tests/Lakona.Game.Server.Generators.Tests.csproj --no-restore --filter "FullyQualifiedName~TypedActorGeneratorTests.Generator_emits_local_and_route_refs_for_actor"
```

Expected: FAIL on the old router-shaped generated source.

- [ ] **Step 3: Generate direct reply dependencies and returns**

Change `AppendClusterHandler` to emit an `IClusterNodeSender` and `LocalActorNodeIdentity` constructor. In both no-result and result cases emit:

```csharp
return await global::Lakona.Game.Server.Actors.RemoteActorGateway.SendReplyAsync(
    _nodeSender,
    _localNode.NodeId,
    envelope.SourceNode,
    envelope.ReplyCorrelationId,
    payload,
    cancellationToken).ConfigureAwait(false);
```

When `ReplyCorrelationId` is null, return `Accepted` after dispatch.

- [ ] **Step 4: Update dispatcher integration fixtures**

Replace router fakes with node-sender fakes and assert the returned failure status is not overwritten after actor execution.

- [ ] **Step 5: Run generator and dispatcher tests**

```powershell
dotnet test tests/Lakona.Game.Server.Generators.Tests/Lakona.Game.Server.Generators.Tests.csproj --no-restore
dotnet test tests/Lakona.Game.Server.Tests/Lakona.Game.Server.Tests.csproj --no-restore --filter "FullyQualifiedName~TypedActorDispatcherTests"
```

Expected: PASS.

- [ ] **Step 6: Commit**

```powershell
git add src/Lakona.Game.Server.Generators/TypedActorGenerator.cs tests/Lakona.Game.Server.Generators.Tests/TypedActorGeneratorTests.cs tests/Lakona.Game.Server.Tests/TypedActorDispatcherTests.cs
git commit -m "Generate node-directed typed actor replies"
```

## Task 6: Remove stale reply-route documentation and APIs

**Files:**

- Modify: `docs/actor.md`
- Modify: `docs/cluster.md`
- Scan: `src`, `tests`, `samples`, `docs`

- [ ] **Step 1: Document the two routing planes**

State explicitly:

```text
Business actor routes are resolved through IRouteDirectory. Framework control
messages and replies addressed to a known NodeId are resolved through
INodeDirectory by IClusterNodeSender. reply/<node-id> is only the destination
node's local handler key and is never registered as a cluster route.
```

- [ ] **Step 2: Run old-surface scans**

```powershell
rg -n "RegisterAsync.*ForReply|PlaceholderEndpoint|SendReplyAsync\(\s*_?router|AskRemoteAsync.*IRouteDirectory" src tests samples docs
```

Expected: no production or test code registers a reply route and no removed signature remains.

- [ ] **Step 3: Run documentation consistency checks**

```powershell
pwsh -NoProfile -File scripts/rpc/check-docs-consistency.ps1
```

Expected: PASS.

- [ ] **Step 4: Commit**

```powershell
git add docs/actor.md docs/cluster.md
git commit -m "Document node-directed actor replies"
```

## Task 7: Apply package versions and validate the independent milestone

**Files:**

- Modify: `src/Lakona.Game.Server/Lakona.Game.Server.csproj`
- Modify: `src/Lakona.Game.Server.Generators/Lakona.Game.Server.Generators.csproj`
- Modify: `src/Lakona.Tool/Lakona.Tool.csproj`

- [ ] **Step 1: Bump each involved package once**

Set:

```xml
Lakona.Game.Server             0.12.0
Lakona.Game.Server.Generators  0.3.0
Lakona.Tool                    0.18.0
```

These are minor+1 with patch reset to zero. Do not bump unaffected packages.

- [ ] **Step 2: Run focused suites sequentially**

```powershell
dotnet test tests/Lakona.Game.Cluster.Tests/Lakona.Game.Cluster.Tests.csproj --no-restore
dotnet test tests/Lakona.Game.Server.Generators.Tests/Lakona.Game.Server.Generators.Tests.csproj --no-restore
dotnet test tests/Lakona.Game.Server.Tests/Lakona.Game.Server.Tests.csproj --no-restore
dotnet test tests/Lakona.Tool.Tests/Lakona.Tool.Tests.csproj --no-restore
```

Expected: all PASS.

- [ ] **Step 3: Run the package graph guard**

```powershell
pwsh -NoProfile -File scripts/nuget/check-package-version-graph.ps1
```

Expected: PASS. If the guard identifies another transitive package, add its minor+1 bump and record the dependency path in the commit body.

- [ ] **Step 4: Run Agar three-node acceptance**

```powershell
pwsh -NoProfile -File scripts/game/ci/test-agar-three-node.ps1
```

Expected: Docker services become healthy; Unity guest login creates `UserActor` on the data node; no `actor-host:create RouteNotFound`; matchmaking reaches battle within five seconds.

- [ ] **Step 5: Run hygiene checks**

```powershell
git diff --check
git status --short
```

Expected: no whitespace errors. The pre-existing user change to `samples/Game.Unity.Agar/docker-compose.yml` remains unstaged unless a later approved Startup Actor migration intentionally edits that file.

- [ ] **Step 6: Commit versions and milestone validation metadata**

```powershell
git add src/Lakona.Game.Server/Lakona.Game.Server.csproj src/Lakona.Game.Server.Generators/Lakona.Game.Server.Generators.csproj src/Lakona.Tool/Lakona.Tool.csproj
git commit -m "Release node-directed actor replies"
```

## Review gates

1. **API/runtime gate after Task 4:** review source/destination identity, reply status propagation, and pending cleanup.
2. **Generator gate after Task 5:** review emitted constructor shape and ensure every generated reply case returns the send status.
3. **Integration gate after Task 7:** review the complete diff with the approved design, exact validation commands/results, package versions, and any skipped checks.

## Completion criteria

- No reply route is registered in `IRouteDirectory`.
- Actor-host create and ordinary actor ask complete through real node-directed infrastructure.
- Failed reply delivery is observable as the handler's returned `ClusterSendStatus`.
- Cancellation, rejection, exception, and timeout do not leak pending registrations; late replies are harmless.
- Focused suites, package graph, docs scan, and Agar three-node E2E pass.
