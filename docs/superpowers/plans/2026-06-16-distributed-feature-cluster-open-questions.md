# Distributed Feature Cluster Resolved Questions

This document resolves the implementation questions raised against
`docs/superpowers/plans/2026-06-16-distributed-feature-cluster.md`.

Development agents must treat these answers as binding. The implementation plan
and `docs/game/distributed-feature-cluster-model.md` have been updated to match
these decisions.

## 1. Implementation Order

Follow the source document order, not the original numeric order of the plan
sections.

The plan now contains an explicit `Execution Order` section. The task sections
are grouped by implementation area; their numeric headings are not the
execution order.

Implementation must proceed in this order:

1. Runtime configuration root, endpoint `RpcServices`, endpoint validation, and
   `Lakona.Game` compatibility tests.
2. Convention-based `LakonaGameFeature` discovery and lifecycle.
3. Endpoint-scoped RPC service binder discovery.
4. Cluster public contract replacement from service terminology to feature
   terminology, including `FeatureName` and `NodeFeatureDescriptor`.
5. Endpoint map registration and feature discovery returning ready,
   non-expired nodes with endpoint data.
6. Feature-addressed request/reply message bus using the separate request/reply
   transport.
7. Gateway-owned client notification route registration, cleanup, and local
   callback relay.
8. Agar three-node acceptance sample.
9. Lakona.Tool generated template and docs cleanup.
10. Package version bumps, final scans, and solution validation.

## 2. Feature Base Class Name

The V1 public base class remains `LakonaGameFeature`.

Do not rename it to `LakonaFeature` in this work. The configuration root is
`Lakona`, but the package and game-server API names keep the `LakonaGame`
prefix unless a separate API-renaming design explicitly changes them.

## 3. Lakona.Tool And Generated Template Scope

This implementation must include tooling and generated-template updates.

Required scope:

- `src/Lakona.Tool`
- `tests/Lakona.Tool.Tests`
- `docs/tool/lakona-tool-generation-architecture.md`
- current docs that describe new runtime configuration as `Lakona.Game` or
  cluster-discoverable capability as service terminology

Generated projects must use:

- `Lakona` as the runtime configuration root
- endpoint-local `RpcServices`
- convention-based feature discovery

Generated projects must not emit:

- endpoint `Name`
- `Lakona:Cluster:Services`
- required fluent `.Feature<...>()` declarations in generated `Program.cs`

## 4. RPC Service Binder API Boundary

`LakonaRpcServiceBinder.Bind` receives `LakonaGameServerRpcContext`.

Binder implementations use:

```csharp
context.Builder.ServiceRegistry
```

Do not introduce another `RpcServiceRegistry` abstraction.

`IRpcServerConfigurator` remains an endpoint-scoped transport-server
configurator. It must identify the endpoint by transport, not by endpoint name.
The V1 interface shape is:

```csharp
public interface IRpcServerConfigurator
{
    string Transport { get; }

    void Configure(LakonaGameServerRpcContext context);
}
```

`LakonaGameServerRpcContext` must expose the concrete endpoint options used to
start that server:

```csharp
public LakonaGameEndpointOptions Endpoint { get; }
```

## 5. Feature Message Bus Request/Reply Transport

Use a separate low-level request/reply transport for feature-addressed
messages.

Do not extend the existing send-only route path to carry replies. These existing
route-addressed interfaces stay send-only:

- `ClusterMessage`
- `IClusterRouter`
- `INodeMessenger`
- `IClusterNodeSender`
- `IClusterMessageHandler`

The feature message bus introduces separate types:

- `FeatureMessageRequest`
- `FeatureMessageReply`
- `IFeatureMessageTransport`
- `IFeatureMessageBus`
- `IFeatureMessageHandler`

`FeatureMessageBus` resolves a ready, non-expired node by `FeatureName`, reads
the selected node's `cluster` endpoint, sends `FeatureMessageRequest` through
`IFeatureMessageTransport`, and returns `FeatureMessageReply`.

## 6. Client Notification Relay And Route Registration

Task 6 owns both local callback lookup and framework-owned route registration.

Required framework behavior:

- Build route keys as
  `client-session:<owner>/<session>/<generation>`.
- Register that route in the cluster route directory after gateway login/session
  bind.
- Remove the route on explicit logout/session termination.
- Allow unexpected disconnect routes to expire through the existing route lease
  model, but do not leave permanent routes.
- Invoke callbacks only on the gateway process that owns the local session.
- Never serialize, store, or send callback objects across nodes.

Because existing `IRouteDirectory` has no route-specific delete operation, this
work must add:

```csharp
ValueTask<RouteUnregisterStatus> UnregisterAsync(
    RouteKey route,
    CancellationToken cancellationToken = default);
```

`RouteUnregisterStatus` must contain `Removed` and `NotFound`.

Agar must call the framework registrar. Agar sample business code must not
write directly to the route directory.

## 7. NuGet Version Bumps

This work changes shippable package code under `src/**`, so package versions
must be bumped in the same implementation branch.

Use these exact target versions:

| Package | Current | Target |
| --- | ---: | ---: |
| `Lakona.Game.Cluster` | `0.1.4` | `0.1.5` |
| `Lakona.Game.Cluster.Rpc` | `0.1.3` | `0.1.4` |
| `Lakona.Game.Cluster.Sql` | `0.1.0` | `0.1.1` |
| `Lakona.Game.Server` | `0.5.4` | `0.5.5` |
