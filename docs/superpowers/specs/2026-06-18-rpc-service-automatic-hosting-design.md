# RPC Service Automatic Hosting Design

Status: approved design
Date: 2026-06-18
Audience: maintainers and implementation agents

## Problem

`samples/Game.Unity.Agar/Server/App/Program.cs` still contains application-level
hosting code that decides whether to register framework and sample services by
manually scanning endpoint `RpcServices`:

```csharp
if (HasRpcService(runtimeOptions, "login") || HasRpcService(runtimeOptions, "player"))
{
    services.AddAgarSampleState();
    services.AddSingleton<SessionDirectory>();
    services.AddSingleton(SelectRealtimeOptions(runtimeOptions));
    services.AddSingleton<GatewayNodeIdentity>();
    services.AddSingleton<MatchmakingMonitor>();
    services.AddSingleton<RoomRuntimeHost>();
    services.AddSingleton<ReliableMatchmakingPublisher>();
    services.AddSingleton<GatewayMatchmakingCoordinator>();
    services.TryAddEnumerable(ServiceDescriptor.Singleton<IRpcSessionLifecycleObserver, PlayerSessionLifecycleObserver>());
    services.AddHostedService<DisconnectedSessionCleanupHostedService>();
}
```

This is the wrong ownership boundary. User-level startup code should not know
which stable services are required to host `login`, `player`, or `battle`.

The earlier idea of adding a user-visible RPC service module or initializer is
also not the desired end state. It only moves the template code into another
file. The target is to remove the need for users to hand-write conditional RPC
service initialization at all.

## Existing Architecture That Must Remain True

`RpcServices` and `Feature` are independent.

- `RpcServices` belong to endpoint configuration. They describe which
  client-facing RPC protocol surfaces an endpoint exposes.
- `Feature` describes node-local runtime capabilities selected at startup.
  Features may participate in cluster feature discovery.
- Configuration must not declare or imply a framework-level
  `RpcService -> Feature` relationship.
- A node with `Feature: []` and endpoint `RpcServices` is valid.
- In the Agar distributed topology, `gateway-1` must remain:

```json
{
  "Lakona": {
    "Node": {
      "Id": "gateway-1"
    },
    "Feature": [],
    "Endpoints": [
      {
        "Transport": "websocket",
        "Serializer": "memorypack",
        "Host": "0.0.0.0",
        "AdvertisedHost": "gateway-1",
        "Port": 20000,
        "Path": "/ws",
        "RpcServices": [ "login", "player" ]
      }
    ]
  }
}
```

No design or implementation step may change that semantic model.

## Goals

- Remove `HasRpcService` from user-level `Program.cs`.
- Remove `SelectRealtimeOptions` from user-level `Program.cs`.
- Avoid adding user-authored RPC service initializer/module boilerplate.
- Let framework defaults register common game-server hosting primitives.
- Let configuration enable, disable, or tune optional framework behavior.
- Keep generated RPC service binders responsible for endpoint protocol binding.
- Keep game projects responsible only for product-specific business services,
  rules, DTOs, and feature implementations.
- Keep `gateway-1` as a pure endpoint/RPC node with `Feature: []`.
- Keep `battle-1` as the node that exposes `battle` RPC and starts
  `battle-runtime`.

## Non-Goals

- Do not introduce a framework-level `RpcService -> Feature` mapping.
- Do not require users to write `RpcServiceInitializer`,
  `RpcServiceModule`, endpoint marker files, or binder configurators.
- Do not reintroduce endpoint names as user-facing concepts.
- Do not make RPC service binders perform `IServiceCollection` registration at
  endpoint bind time.
- Do not do database, network, hotfix load, or cluster I/O during DI
  registration.
- Do not turn sample-specific matchmaking policy, room rules, account logic, or
  persistence schema into framework features.

## Design

### Framework Hosting Defaults

`LakonaGameServer.RunAsync` and `services.AddLakonaGameServer()` should provide
the common hosting services required by generated game servers without
user-authored conditional registration.

The default game-server host must register these framework-owned primitives:

- actor runtime
- game session directory and session resume APIs
- RPC session lifecycle observer for game sessions
- client-session route registration and notification relay
- reliable push outbox and acknowledgement service
- `ILakonaGameServer`

Hosted background behavior must be controlled through configuration instead of
manual template code. Session cleanup is the immediate case:

```json
{
  "Lakona": {
    "Sessions": {
      "Cleanup": {
        "Enabled": true,
        "IntervalSeconds": 30,
        "DisconnectedRetentionSeconds": 120
      }
    }
  }
}
```

When `Enabled` is omitted, generated game-server hosts should use the framework
default. The default should favor the normal generated-server experience:
session cleanup is on unless explicitly disabled.

Actor runtime options should also be configuration-driven:

```json
{
  "Lakona": {
    "Actors": {
      "CallTimeoutSeconds": 5,
      "SlowMessageThresholdSeconds": 1
    }
  }
}
```

Generated samples should not need to call
`services.AddLakonaGameServerActors(options => ...)` only to set standard
timeouts.

### RPC Service Binding

Generated RPC service binders remain the endpoint protocol binding mechanism.

- Endpoint `RpcServices` select which binders are exposed on that endpoint.
- Unknown RPC service names still fail startup.
- Duplicate RPC service names on the same endpoint still fail startup.
- A binder still binds once per endpoint that lists its service name.
- Binder `Bind` methods bind methods into `RpcServiceRegistry`; they do not
  register `IServiceCollection` services.

The implementation may use internal framework helper types to keep startup
code organized, but no new user-visible initializer or marker type should be
required in generated projects.

### Generated Hotfix Service Boundary

Generated stable server bindings should continue to inject stable framework
services into hotfix proxies, especially `ILakonaGameServer`,
`IHotfixServiceInvoker`, and `IActorRuntime`.

When a hotfix service needs session, callback, reconnect, reliable push, or
acknowledgement behavior, the preferred dependency is `ILakonaGameServer` or
another framework-owned API. The sample should not build its own parallel
session and reliable-push stack unless it is demonstrating domain-specific
logic that the framework cannot own.

### Endpoint Identity

`SelectRealtimeOptions` must be removed because it mixes two different
concepts:

- control endpoint identity: the endpoint that owns login/control connections
  and control callbacks, such as `gateway-1` websocket
- runtime endpoint identity: the endpoint that owns realtime battle attachment
  and input, such as `battle-1` KCP

The framework or sample should expose typed endpoint descriptors instead of
choosing a generic `LakonaGameEndpointOptions` through fallback logic.

Required behavior:

- A control service must describe the actual endpoint that accepted the
  control connection.
- A battle runtime node must describe the actual runtime endpoint that clients
  should attach to.
- A missing runtime endpoint must fail validation for nodes that start
  `battle-runtime`; it must not silently fall back to a fake KCP endpoint.
- A pure gateway node must not require a local KCP endpoint.

### Runtime Gateway Selection

The Agar distributed flow must match the existing architecture document:

```txt
Unity client
  -> gateway-1 websocket player RPC
  -> gateway handler sends matchmaking command to data-1
  -> data-1 matchmaking updates queue
  -> data-1 selects a battle-runtime node through feature discovery
  -> data-1 asks battle-1 to allocate a room
  -> battle-1 creates room runtime and registers room route
  -> data-1 persists assignment
  -> gateway reliable-pushes matched update to client
```

Therefore `RuntimeGateway` must not be derived from the player's
`ControlGateway` in distributed mode. That derivation only works in the local
single-process sample where websocket control and KCP battle endpoints happen
to live in the same process.

The implementation must make this distinction explicit:

- `ControlGateway` records where the control session is bound.
- `RuntimeGateway` records where the battle runtime is owned.
- `matchmaking` or the room allocation path chooses `RuntimeGateway` from
  discovered `battle-runtime` nodes, not from control endpoint fallback.

### Agar Sample Target Shape

After the design is implemented, the stable Agar `Program.cs` should be reduced
to high-level host composition only. It may still register sample-specific
features and sample-specific state abstractions, but it must not scan endpoint
`RpcServices`.

Target shape:

```csharp
return await LakonaGameServer.RunAsync(args, server => server
    .AddServices((services, configuration) =>
    {
        services.AddMessageRecording();
        services.AddLakonaGameRuntimeValidation();

        services.AddLakonaGame(configuration, [
            typeof(DatabaseFeature),
            typeof(StateStoreFeature),
            typeof(MatchmakingFeature),
            typeof(LeaderboardFeature),
            typeof(BattleRuntimeFeature)
        ]);
    })
    .UseGeneratedHotfixServices());
```

The exact remaining sample registrations may change during implementation, but
these rules are fixed:

- no `HasRpcService`
- no `SelectRealtimeOptions`
- no user-authored RPC service initializer/module
- no control gateway Feature added only to support `login` or `player`
- `gateway-1` remains `Feature: []`

## Configuration Contract

Implementation should prefer additive configuration under `Lakona`.

Minimum required additions:

- `Lakona:Sessions:Cleanup:Enabled`
- `Lakona:Sessions:Cleanup:IntervalSeconds`
- `Lakona:Sessions:Cleanup:DisconnectedRetentionSeconds`
- `Lakona:Actors:CallTimeoutSeconds`
- `Lakona:Actors:SlowMessageThresholdSeconds`

Optional runtime endpoint configuration may be added only if feature discovery
cannot provide enough information. If added, it must not create a
`RpcService -> Feature` relationship and must not require pure gateway nodes to
declare KCP endpoints.

## Validation Requirements

Framework or sample tests must cover:

- `gateway-1` can build with `Feature: []` and websocket `RpcServices`
  `[ "login", "player" ]`.
- `gateway-1` does not require a local KCP endpoint.
- `battle-1` can build with `Feature: [ "battle-runtime" ]` and KCP
  `RpcServices` `[ "battle" ]`.
- `battle-runtime` fails clearly when no runtime-capable endpoint exists.
- unknown configured RPC service names still fail startup.
- duplicate endpoint RPC service names still fail startup.
- user-level Agar `Program.cs` contains no `HasRpcService` or
  `SelectRealtimeOptions`.
- generated projects do not contain user-authored RPC service initializer,
  endpoint marker, or binder configurator templates.

Source-scan tests are acceptable for guarding removed template patterns, but
runtime-contract tests must also verify the important startup paths.

## Implementation Notes

This design intentionally avoids specifying exact internal class names. The
implementation should choose names that match the existing codebase.

Useful existing anchors:

- `LakonaGameServer.RunAsync`
- `LakonaGameServerServiceCollectionExtensions.AddLakonaGameServer`
- `SessionServiceCollectionExtensions`
- `ReliablePushServiceCollectionExtensions`
- `LakonaEndpointRpcServerConfigurator`
- generated hotfix service binders
- `LakonaGameEndpointCatalog`
- `BattleRuntimeFeature`

If internal orchestration helpers are introduced, they must remain framework
implementation details. They must not become a new user edit zone.

## Acceptance Criteria

The design is complete when:

- Agar stable startup no longer manually scans configured RPC services.
- Common game hosting services are available through framework defaults and
  configuration.
- Pure gateway nodes can host `login` and `player` RPC services with
  `Feature: []`.
- Battle runtime ownership is selected independently of the control gateway.
- Runtime endpoint selection has no silent fake fallback.
- Documentation continues to state that `RpcServices` and `Feature` are
  independent.
