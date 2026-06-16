# Distributed Feature And Cluster Model

## Purpose

Lakona.Game needs one long-term distributed runtime model that is simple enough
for generated projects, explicit enough for production deployment, and concrete
enough to be validated by `samples/Game.Unity.Agar`.

This model uses Agar as the first end-to-end acceptance sample, but the
framework only absorbs generic runtime concepts. Game projects still own
account policy, matchmaking policy, room rules, gameplay simulation, persistence
schema, and product DTOs.

## Core Concepts

The public configuration root is `Lakona`.

The distributed model has four user-facing concepts:

| Concept | Meaning |
| --- | --- |
| Node | One running server process and the deployment identity registered in the cluster. |
| Feature | A local startup unit and, when discoverable, a cluster-discoverable node capability. |
| Endpoint | A client-facing transport listener such as WebSocket or KCP. |
| RpcService | The client-facing RPC protocol surface exposed through an endpoint. |

Cluster is the framework-owned node-to-node substrate. It provides node
membership, endpoint discovery, feature discovery, route lookup, message
routing, and remote actor plumbing. Cluster is not a separate user-facing
service list.

## Configuration Shape

All runtime configuration is under `Lakona`.

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
        "Host": "0.0.0.0",
        "Port": 20000,
        "Path": "/ws",
        "RpcServices": [ "login", "player" ]
      }
    ],
    "Cluster": {
      "Endpoint": "tcp://10.0.0.2:21002",
      "Seeds": [ "tcp://10.0.0.1:21001" ]
    }
  }
}
```

### Node

`Lakona:Node:Id` is required. It is the stable identity for the current
process. Cluster also uses it as the node id.

### Feature

`Lakona:Feature` controls application feature activation.

Rules:

- Omitted `Feature` enables all discovered application features, ordered by
  feature name.
- An empty array enables no application features.
- A string array enables only the listed features, in array order.
- Unknown features fail startup.
- Duplicate features fail startup.

Feature order is intentionally visible in configuration. If a project needs a
database feature to initialize first, it writes it first:

```json
{
  "Lakona": {
    "Feature": [
      "database",
      "state-store",
      "matchmaking",
      "leaderboard"
    ]
  }
}
```

Feature types are discovered by convention from server assemblies. A
`DatabaseFeature` type resolves to `database`, `StateStoreFeature` resolves to
`state-store`, and `BattleRuntimeFeature` resolves to `battle-runtime`.

Feature is also the cluster discovery unit. A discoverable feature on a ready
node is registered in the node directory so other nodes can find nodes with that
capability.

### Endpoint

`Lakona:Endpoints` lists client-facing transport listeners.

Endpoint rules:

- `Transport` is required.
- A node can have at most one endpoint for a given transport.
- Endpoint `Name` is intentionally not part of the model.
- WebSocket endpoints require `Path`.
- KCP endpoints must not set `Path`.
- `RpcServices` can only be configured on an endpoint.
- Duplicate `RpcServices` in the same endpoint fail startup.
- Unknown `RpcServices` fail startup.

The cluster endpoint is not listed in `Endpoints`; it is configured as
`Lakona:Cluster:Endpoint` because it is a node-to-node channel, not a
client-facing business endpoint.

### RpcService

`RpcService` describes which client-facing RPC protocol surfaces an endpoint
exposes.

RpcService is intentionally independent from Feature. Configuration does not
declare a `RpcService -> Feature` target. The RPC handler owns business
composition and can call local features, remote features, remote actors, the
message bus, databases, or other project services.

Example:

```json
{
  "Transport": "websocket",
  "Host": "0.0.0.0",
  "Port": 20000,
  "Path": "/ws",
  "RpcServices": [ "login", "player" ]
}
```

An RPC service binder is discovered by name:

```csharp
[LakonaRpcService("login")]
public sealed class LoginRpcServiceBinder : LakonaRpcServiceBinder
{
    public override void Bind(LakonaRpcServiceBindingContext context)
    {
        // Bind generated RPC services into the endpoint registry.
    }
}
```

If `login` is not listed under an endpoint, the binder can exist in the
assembly but is not exposed on that endpoint.

## Feature Lifecycle

Features support both dependency registration and runtime lifecycle hooks.

```csharp
public abstract class LakonaFeature
{
    public virtual string Name => FeatureNameConventions.FromType(GetType());

    public virtual bool Discoverable => true;

    public virtual void ConfigureServices(LakonaFeatureContext context)
    {
    }

    public virtual ValueTask StartAsync(
        LakonaFeatureRuntimeContext context,
        CancellationToken cancellationToken = default)
    {
        return default;
    }

    public virtual ValueTask StopAsync(
        LakonaFeatureRuntimeContext context,
        CancellationToken cancellationToken = default)
    {
        return default;
    }
}
```

Lifecycle semantics:

- `ConfigureServices` only registers services. It should not perform network or
  database I/O.
- `StartAsync` runs after the host is built and before client listeners begin
  accepting traffic.
- `StartAsync` is for schema readiness checks, migrations, cache warmup, route
  registration, feature-owned background loops, and other startup work.
- `StopAsync` runs during shutdown in reverse feature order.
- If a feature fails during `StartAsync`, startup fails and already started
  features are stopped in reverse order.
- If a feature fails during `StopAsync`, the failure is logged and shutdown
  continues.

Features that are startup dependencies but should not be cluster-discoverable
can opt out:

```csharp
public sealed class DatabaseFeature : LakonaFeature
{
    public override bool Discoverable => false;
}
```

`database` is a good example: it creates local connection factories and
repository dependencies for the data node, but other nodes should not discover
it as a business command target.

## Cluster Registration

The cluster directory registers nodes, endpoints, features, state, epoch, and
lease. It does not register cluster services.

Long-term node record shape:

```txt
NodeRecord
  ClusterName
  NodeId
  NodeEpoch
  State
  Endpoints
  Features
  LeaseExpiresAt
```

Feature descriptor shape:

```txt
NodeFeatureDescriptor
  Name
  Metadata
```

There is no `Instance` field. `NodeId` and `NodeEpoch` identify the process
instance. A feature says what the node can do; the node identity says which
process currently provides it.

Framework-managed capabilities use reserved `lakona.` feature names where they
must be visible internally, for example `lakona.node-directory`,
`lakona.route-directory`, and `lakona.message-bus`. Ordinary application
configuration does not list framework system features.

## Feature Discovery

Feature discovery answers:

> Which ready nodes currently provide this feature, and how can they be reached?

Suggested API shape:

```csharp
public interface IClusterNodeDiscovery
{
    ValueTask<IReadOnlyList<ClusterNodeDescriptor>> ListAsync(
        FeatureName feature,
        CancellationToken cancellationToken = default);

    ValueTask<ClusterNodeDescriptor?> AnyAsync(
        FeatureName feature,
        CancellationToken cancellationToken = default);
}
```

`ClusterFeature` should be renamed to `FeatureName` so discovery terminology
matches startup terminology.

Feature discovery is not actor placement. It finds nodes by capability:

```txt
battle-runtime -> node battle-1
```

Route directory finds concrete actors or message routes:

```txt
actor:room/room-123 -> node battle-1
```

## Message Bus And Remote Actors

Cross-node communication has two layers over the same `ClusterMessage`
delivery semantics.

Feature-addressed message bus:

```csharp
await messageBus.SendToFeatureAsync(
    "battle-runtime",
    new AllocateRoomCommand(...),
    cancellationToken);
```

This finds a node by feature, then sends a request or command to that node. It
is appropriate for service-level commands such as allocating a battle room,
enqueueing matchmaking, or recording a match settlement.

Actor-addressed remote actor calls:

```csharp
await rooms.Local(roomId).SubmitInputAsync(input, cancellationToken);
await rooms.Remote(nodeId, roomId).SubmitInputAsync(input, cancellationToken);
await rooms.Get(roomId).SubmitInputAsync(input, cancellationToken);
```

This finds a concrete actor route and dispatches through a
`ClusterActorEnvelope`. It is appropriate for addressable state units such as
rooms, player sessions, and leaderboard actors.

Boundary:

```txt
Feature message bus: who can handle this kind of command?
Remote actor: where is this concrete object currently owned?
```

The first version is request/reply and point-to-point. It does not introduce
durable pub/sub, topics, consumer groups, offsets, or room migration.

## Failure Model

Cluster delivery uses structured statuses at low levels and typed exceptions at
business-facing layers.

Initial statuses:

- `FeatureNotFound`
- `RouteNotFound`
- `NodeUnavailable`
- `Timeout`
- `Backpressure`
- `HandlerUnavailable`
- `Expired`
- `SerializationFailed`
- `DeserializationFailed`
- `Rejected`

Business-facing generated APIs should normally throw typed exceptions instead
of requiring every caller to switch over status codes.

## Startup Order

Startup order is framework-owned and deterministic:

1. Load `Lakona` configuration.
2. Resolve node identity.
3. Resolve endpoints by transport.
4. Discover application features and RPC service binders.
5. Register framework-managed services.
6. Run enabled feature `ConfigureServices` in resolved feature order.
7. Build the host.
8. Start framework cluster and message bus services.
9. Run enabled feature `StartAsync` in resolved feature order.
10. Register node endpoints and discoverable features in the cluster directory.
11. Bind configured endpoint RPC services by transport.
12. Start transport listeners.
13. On shutdown, stop features in reverse resolved order.

Transport listeners start after feature startup and RPC binding so invalid
configuration fails before the node accepts client traffic.

## Agar Acceptance Topology

The first end-to-end acceptance sample is a three-node Agar deployment.

### Data Node

```txt
node data-1
features:
  database
  state-store
  matchmaking
  leaderboard
endpoints:
  cluster tcp://10.0.0.1:21001
```

The data node owns database connections, persistent state access, matchmaking
policy, room assignment state, and leaderboard updates.

### Gateway Node

```txt
node gateway-1
features:
  []
endpoints:
  websocket ws://game.example.com:20000/ws
rpc services:
  login
  player
cluster:
  tcp://10.0.0.2:21002
```

The gateway node owns WebSocket connections, client callback bindings, and
transport admission. It does not connect to the database and does not own
matchmaking policy. Its RPC handlers decide how to call data-node features.

### Battle Node

```txt
node battle-1
features:
  battle-runtime
endpoints:
  kcp kcp://battle.example.com:20001
rpc services:
  battle
cluster:
  tcp://10.0.0.3:21003
```

The battle node owns KCP realtime connections, active room simulation, local
realtime callback bindings, world-state push, and match settlement publication.

## Agar Data Flow

Login:

```txt
Unity client
  -> gateway-1 websocket login RPC
  -> gateway handler calls data-1 state-store through project-owned composition
  -> gateway stores control callback locally
  -> login reply returns over websocket
```

Matchmaking:

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

Realtime attach:

```txt
Unity client
  -> battle-1 KCP battle RPC
  -> battle-1 validates session and room state through data-node calls
  -> battle-1 binds realtime callback locally
  -> client submits input over KCP
```

Battle:

```txt
Unity client
  -> battle-1 KCP input
  -> battle-1 room runtime ticks simulation
  -> battle-1 pushes world state over local realtime callback
```

Settlement:

```txt
battle-1
  -> sends settlement to data-1 state-store and leaderboard features
  -> clears or expires room route
  -> pushes match end to realtime clients
```

## Agar Acceptance Criteria

- Three independent processes start with `Lakona` configuration.
- Node directory can see `data-1`, `gateway-1`, and `battle-1`.
- Node directory can discover `state-store`, `matchmaking`, `leaderboard`, and
  `battle-runtime` features where appropriate.
- `gateway-1` exposes only WebSocket `login` and `player` RPC services.
- `battle-1` exposes only KCP `battle` RPC service.
- A Unity client logs in through `gateway-1`.
- Matchmaking allocates a room on `battle-1`.
- The client receives the battle KCP endpoint and attaches to `battle-1`.
- `battle-1` pushes world state.
- `battle-1` settles a match and writes results through `data-1`.
- If `battle-1` stops, new matches are not assigned to its expired node record.

## Out Of Scope For V1

- Transparent distributed actors.
- Actor migration.
- Room migration after battle-node failure.
- Durable pub/sub.
- Consumer groups or stream offsets.
- Automatic load balancing beyond a simple project-owned selection policy.
- Multiple endpoints with the same transport in one node.
- Complex feature dependency graphs.
- Framework-owned matchmaking, account, room, or persistence policy.

## Migration Direction

Current configuration and APIs still use `Lakona.Game` in places. The long-term
runtime root is `Lakona`. Migration should update:

- configuration binding paths
- diagnostics paths
- check output
- generated project templates
- samples
- docs

Current cluster code also uses service terminology. Migration should remove the
public cluster service concept:

- replace `NodeServiceDescriptor` with `NodeFeatureDescriptor`
- replace `NodeRegistration.Services` with `NodeRegistration.Features`
- replace `NodeRecord.Services` with `NodeRecord.Features`
- replace service graph diagnostics with feature diagnostics
- remove `Lakona:Cluster:Services` from user-facing configuration
- rename `ClusterFeature` to `FeatureName`

DI services remain DI services. Only cluster membership and discovery should
avoid the service terminology.
