# Cluster

## Purpose

Lakona's game framework needs one distributed runtime model that is simple
enough for generated projects, explicit enough for production deployment, and
concrete enough to be validated by `samples/Game.Unity.Agar`.

This model uses Agar as the first end-to-end acceptance sample, but the
framework only absorbs generic runtime concepts. Game projects still own
account policy, matchmaking policy, room rules, gameplay simulation, persistence
schema, and product DTOs.

This document is the implementation contract. Implementations must follow
the rules here unless a later documentation change explicitly supersedes them.

Normative words have their usual meaning:

- **Must** and **must not** are required behavior.
- **May** means permitted behavior, not required behavior.

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

Code, configuration, diagnostics, generated templates, and docs must use
Feature for cluster-discoverable node capability. Cluster membership and
discovery must not expose a separate service list.

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
        "Serializer": "json",
        "Host": "0.0.0.0",
        "Port": 20000,
        "Path": "/ws",
        "RpcServices": [ "login", "player" ]
      }
    ],
    "Cluster": {
      "Endpoint": "tcp://10.0.0.2:21002",
      "Serializer": "json",
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

Feature order is intentionally visible in configuration. If a project needs
state-store startup before matchmaking, it writes it first:

```json
{
  "Lakona": {
    "Feature": [
      "state-store",
      "matchmaking",
      "leaderboard"
    ]
  }
}
```

Feature types are discovered by convention from server assemblies. A
`StateStoreFeature` resolves to `state-store`, and `BattleRuntimeFeature`
resolves to `battle-runtime`.

Feature names are application capability names. The framework does not attach
special meaning to names such as `state-store`; a project could name the same
capability `lobby` if that is the product language. What matters to the
framework is that the name is stable, discoverable, and selected consistently
in configuration and generated clients.

The V1 feature name convention is:

1. The type name must end with `Feature`.
2. Remove the `Feature` suffix.
3. Convert PascalCase or acronym words to lower-case kebab-case.
4. Reject empty names.
5. Reject generated names that collide case-insensitively.

Examples:

| Type | Feature name |
| --- | --- |
| `StateStoreFeature` | `state-store` |
| `BattleRuntimeFeature` | `battle-runtime` |
| `HTTPGatewayFeature` | `http-gateway` |

Feature is also the cluster discovery unit. A discoverable feature on a ready
node is registered in the node directory so other nodes can find nodes with that
capability.

Co-locating features on one node does not create ownership between them. For
example, Agar may configure `matchmaking` and `state-store` on `data-1`, but
`MatchmakingFeature` still owns creation and command handling for
`MatchmakingActor`; state-store remains a separate feature that happens to run
in the same process.

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
client-facing business endpoint. When configured, the framework starts a
cluster RPC server on that endpoint. The server binds node-directory,
route-directory, and feature-message RPC handlers only when the corresponding
local services exist in DI.

`Lakona:Cluster:Serializer` is required whenever `Lakona:Cluster` is
configured. It selects the node-to-node RPC payload serializer for cluster
protocol DTOs. The DTOs in `Lakona.Game.Cluster.Rpc` are serializer-neutral;
serializer packages provide the concrete encoding support. Supported values
are `json` and `memorypack`. Every node that exchanges cluster RPC traffic
must use the same cluster serializer because node-directory calls,
route-directory calls, feature-addressed messages, route-addressed messages,
client-notification relay commands, and remote actor payloads all follow this
setting. Mixed client-facing endpoint serializers are allowed, but the cluster
channel has one serializer per communicating cluster.

Generated projects that emit cluster configuration copy the user's
`--serializer` choice into `Lakona:Cluster:Serializer`. This keeps business
RPC payloads, cluster protocol payloads, feature-addressed message payloads,
client-notification relay commands, and remote actor payloads aligned with the
same project-level serializer choice. Client-facing framework control messages
such as handshake, heartbeat, reliable push ack, and session termination notice
remain encoded with `LakonaInternalCodec`.

Do not add serializer-specific attributes or package references back to
`Lakona.Game.Cluster.Rpc` DTOs. MemoryPack cluster support belongs in
`Lakona.Game.Cluster.Rpc.MemoryPack`, where repository-owned generation and the
committed formatter schema define the built-in cluster wire layout without
making JSON users restore MemoryPack generator assets.

Built-in cluster RPC payload layouts are package-set contracts. The early
framework does not support rolling mixed-version cluster nodes that rely on old
and new built-in cluster DTO bytes being mutually readable; deploy nodes with
matching `Lakona.Game.Cluster.Rpc` and `Lakona.Game.Cluster.Rpc.MemoryPack`
packages.

`Lakona:Cluster:Seeds` is the public bootstrap input for directory access. If
a node has seeds but no local `INodeDirectory` or `IRouteDirectory`
registration, the framework creates remote directory clients that call the
seed node over the cluster RPC endpoint. Local directory registrations, such as
the data node's durable database-backed directory, take precedence. A seed that
resolves to the current node's own `Lakona:Cluster:Endpoint` is not used to
create remote directory clients; directory-owner nodes must provide local
directory implementations instead of recursively calling their own cluster RPC
endpoint.

### Cluster Serializer Wiring

Built-in cluster endpoint wiring creates one cluster serializer from
`Lakona:Cluster:Serializer` and uses that same serializer for the cluster
client factory, cluster RPC server, feature-message transport,
client-notification relay commands, and the default `RpcRemoteActorSerializer`.
The configured cluster serializer wins over earlier bare `IRpcSerializer`
registrations in built-in cluster wiring.

When `Lakona:Cluster:Serializer` is `memorypack`, server cluster wiring uses
the generated built-in cluster MemoryPack formatters from
`Lakona.Game.Cluster.Rpc.MemoryPack`. Advanced manual MemoryPack cluster hosts
that bypass the built-in server cluster wiring should create the cluster RPC
serializer with `ClusterRpcMemoryPack.CreateSerializer()` so those framework
formatters are registered before cluster traffic is serialized.

Later app-specific or endpoint-specific `IRpcSerializer` registrations must
not change the already configured cluster channel. Cluster infrastructure must
use the cluster serializer holder created by endpoint wiring, not an
unrelated bare serializer service that may belong to another RPC surface.

Low-level direct use of `LakonaClusterRpcServerConfigurator` must bind the
server to the same serializer selected by `Lakona:Cluster:Serializer`, or use
the full `AddLakonaGameClusterEndpoint()` wiring. Falling back to an arbitrary
DI `IRpcSerializer` contradicts the cluster configuration contract and can
make the server speak a different payload format from cluster clients.

Projects may provide a custom `IRemoteActorSerializer`, but that is an
explicit compatibility choice for the whole communicating cluster. The built-in
default adapts the configured cluster serializer and does not introduce a
separate JSON-only remote actor format.

### Node Directory Storage

The node directory stores live cluster membership metadata. It is framework
infrastructure, not gameplay state, account data, matchmaking state,
leaderboard state, route ownership, or a durable event log.

For SQL-backed node directories, the framework owns the `lakona_cluster_nodes`
table contract and ships dialect-specific initial schema scripts through
`Lakona.Game.Cluster.Sql`. Production app startup must not be the default place
where this table is created. Production deployments should apply the schema
through a controlled migration, DBA step, or admin bootstrap job, then let app
startup verify readiness without DDL permissions.

`SqlNodeDirectorySchema.EnsureCreatedAsync` is allowed for tests, local
development, and explicit admin/bootstrap tooling. Production app code should
prefer `SqlNodeDirectorySchema.VerifyReadyAsync`, which checks that the table
and required columns are queryable without attempting `CREATE TABLE`.

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
  "Serializer": "json",
  "Host": "0.0.0.0",
  "Port": 20000,
  "Path": "/ws",
  "RpcServices": [ "login", "player" ]
}
```

An RPC service binder is discovered by an explicit attribute:

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

V1 binder rules:

- Every RPC service binder must inherit `LakonaRpcServiceBinder`.
- Every RPC service binder must have exactly one `LakonaRpcServiceAttribute`.
- `LakonaRpcServiceAttribute.Name` must be lower-case kebab-case.
- Duplicate RPC service names fail startup.
- Configured RPC service names are matched case-insensitively, but resolved
  names are normalized to lower-case.
- A binder is bound once per endpoint that lists its service name.
- The framework does not infer a binder name from the C# type name in V1.
- `LakonaRpcServiceBinder.Bind` receives `LakonaGameServerRpcContext`.
- Binder implementations use `context.Builder.ServiceRegistry`; V1 must not
  introduce another `RpcServiceRegistry` abstraction.
- `IRpcServerConfigurator` remains an endpoint-scoped transport-server
  configurator. It must identify the endpoint by transport, not by endpoint
  name.

## Feature Lifecycle

Features support both dependency registration and runtime lifecycle hooks.

Those hooks are stable process lifecycle hooks. They are not hotfix reload
hooks and they do not make Feature classes replaceable. Stable
`LakonaGameFeature` is framework infrastructure; user-authored game feature
declarations live in the hotfix assembly and use `HotfixGameFeature` lifecycle
to create timers or LakonaTimer-backed runtime loops.

```csharp
public abstract class LakonaGameFeature
{
    public virtual string Name => FeatureNameConventions.FromType(GetType());

    public virtual bool Discoverable => true;

    public virtual void ConfigureServices(LakonaGameFeatureContext context)
    {
    }

    public virtual ValueTask StartAsync(
        LakonaGameFeatureContext context,
        CancellationToken cancellationToken = default)
    {
        return default;
    }

    public virtual ValueTask StopAsync(
        LakonaGameFeatureContext context,
        CancellationToken cancellationToken = default)
    {
        return default;
    }
}
```

Lifecycle semantics:

- `ConfigureServices` only registers services. It must not perform network or
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

`StartAsync` and `StopAsync` must not contain replaceable game decisions.
They may verify infrastructure, warm caches, or register routes. User-authored
runtime loops are created by feature lifecycle as timers or LakonaTimer-backed
runtime loops.

Features that are startup dependencies but must not be cluster-discoverable
can opt out:

```csharp
public sealed class FrameworkDatabaseStartup : LakonaGameFeature
{
    public override bool Discoverable => false;
}
```

The V1 public base class remains `LakonaGameFeature`. The configuration root is
`Lakona`, but the runtime package and game-server API names keep the
`LakonaGame` prefix unless a separate API-renaming design explicitly changes
them.

Local persistence setup is a good example of non-discoverable startup work: it
creates local connection factories and repository dependencies for a node, but
other nodes must not discover it as a business command target.

V1 feature metadata rules:

- `Discoverable == true` means the feature is included in node registration
  after `StartAsync` succeeds.
- `Discoverable == false` means the feature affects only local startup and is
  not visible through cluster discovery.
- A feature is never discoverable before its `StartAsync` completes.
- When a feature provides metadata, it is copied as a string dictionary into
  `NodeFeatureDescriptor.Metadata`.
- Feature metadata must not contain high-cardinality per-player or per-room
  values.

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

Endpoint registration uses a normalized endpoint map:

| Key | Source |
| --- | --- |
| `cluster` | `Lakona:Cluster:Endpoint` |
| `websocket` | The advertised WebSocket endpoint from `Lakona:Endpoints[]` |
| `kcp` | The advertised KCP endpoint from `Lakona:Endpoints[]` |
| `tcp` | The advertised TCP endpoint from `Lakona:Endpoints[]`, when present |

Endpoint keys are lower-case transport names, except the reserved `cluster`
key. Endpoint values are externally reachable advertised endpoint URIs.

Nodes with a cluster configuration must keep their node registration alive.
After startup registration, the framework refreshes the node lease before it
expires. `Refreshed` heartbeats update the local lease, `NodeNotFound` and
`Expired` heartbeats re-register the node, and `EpochMismatch` stops the
heartbeat loop because another process owns the node identity. Graceful
shutdown marks the registered node epoch dead and clears route ownership for
that node epoch when a route directory is available.

## Feature Discovery

Feature discovery answers:

> Which ready nodes currently provide this feature, and how can they be reached?

V1 API shape:

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

`ClusterFeature` must be renamed to `FeatureName` so discovery terminology
matches startup terminology.

`ClusterNodeDescriptor` must include the selected node id, state, endpoint map,
features, and labels. Feature-addressed message bus implementations use
`ClusterNodeDescriptor.Endpoints["cluster"]` to reach the selected node.

The implementation must not keep `ClusterFeature` as a public type. If an
internal compatibility shim is temporarily required, it must not appear in
public docs, generated code, or new APIs.

Feature discovery is not actor placement. It finds nodes by capability:

```txt
battle-runtime -> node battle-1
```

Route directory finds concrete actors or message routes:

```txt
actor:room/room-123 -> node battle-1
```

## Message Bus And Remote Actors

Cross-node communication has two separate delivery families:

- Route-addressed sends use the existing send-only `ClusterMessage`,
  `IClusterRouter`, `INodeMessenger`, and `IClusterMessageHandler` path.
- Feature-addressed request/reply uses a new low-level request/reply transport
  over the selected node's `cluster` endpoint.

V1 must not force request/reply payloads into the existing send-only route
interfaces.

Feature-addressed message bus:

```csharp
await messageBus.SendToFeatureAsync(
    "battle-runtime",
    new AllocateRoomCommand(...),
    cancellationToken);
```

This finds a node by feature, then sends a request or command to that node. Use
feature-addressed messages for service-level commands such as allocating a
battle room, enqueueing matchmaking, or recording a match settlement.

Feature messages are the boundary for capability-level work: placement,
creation, capacity checks, idempotency, and cross-node command admission. They
are not the normal permanent proxy for every method on an already-created
actor. Once a concrete actor exists, callers should use generated actor refs,
the actor runtime, or route-addressed actor calls.

Typed feature commands encode `FeatureCommandId` as an invariant-culture decimal
string in `FeatureMessageRequest.Kind`. Blank values, non-integers, zero,
negative values, and overflow values are rejected before deserialization with
`ClusterSendStatus.Rejected`.

`IFeatureCommandClient.SendAsync` selects any ready node that advertises the
feature. `SendToNodeAsync` sends the same typed command to an already selected
`ClusterNodeDescriptor`, which is the correct path after placement logic has
chosen a specific owner node.

Do not hard-code business command kind strings such as
`"agar.user.ensure"` or `"agar.room.allocate"` in services or samples. Stable
command identity should come from typed command contracts, attributes, or
generated constants, and business code should call generated typed clients when
available. `typeof(TRequest).FullName` is also not a long-term wire identity.

V1 feature-addressed delivery:

1. Resolve candidate nodes with `IClusterNodeDiscovery.AnyAsync(feature)`.
2. Use the candidate node's `cluster` endpoint.
3. Send a `FeatureMessageRequest` to the selected node through the new
   request/reply transport, serialized with `Lakona:Cluster:Serializer`.
4. Dispatch the message to a registered local feature message handler.
5. Return a `FeatureMessageReply` or a structured failure.

Selection policy is intentionally small in V1. `AnyAsync` must return a ready
node with a non-expired lease. Project-owned code may layer its own capacity or
region policy on top of `ListAsync`.

Actor-addressed remote actor calls:

```csharp
await rooms.Local(roomId).SubmitInputAsync(input, cancellationToken);
await rooms.Remote(nodeId, roomId).SubmitInputAsync(input, cancellationToken);
await rooms.Get(roomId).SubmitInputAsync(input, cancellationToken);
```

This finds a concrete actor route and dispatches through a
`ClusterActorEnvelope`. Use actor-addressed calls for addressable state units
such as rooms, player sessions, and leaderboard actors.

Generated remote actor refs serialize method request and reply payloads with
the same serializer selected by `Lakona:Cluster:Serializer`. The public
`IRemoteActorSerializer` remains the actor-facing abstraction. Active cluster
endpoint wiring registers the default adapter over the configured cluster
`IRpcSerializer` instead of introducing a separate JSON-only actor serializer.

Boundary:

```txt
Feature message bus: who can handle this kind of command?
Remote actor: where is this concrete object currently owned?
```

The first version is request/reply and point-to-point. It does not introduce
durable pub/sub, topics, consumer groups, offsets, or room migration.

Remote actor envelopes and their request/reply payloads use
`Lakona:Cluster:Serializer`, not endpoint-local client RPC serializers.

## Client Notification Relay

Client callbacks and transport sessions are process-local objects. A remote
actor or feature must not hold or invoke a WebSocket or KCP callback object that
lives on another node.

Client-directed notifications cross nodes through a gateway-owned route:

```txt
client session route
  client-session:<playerId>/<sessionId>/<generation> -> gateway node
```

When a client logs in through a WebSocket gateway, the gateway registers a
client-session route for that `GameSessionKey`. A player actor, room actor, or
feature on another node sends a notification to the session route. The gateway
receives the cluster message, looks up the local connection and callback, and
invokes the callback in its own process.

The route metadata must not contain callback objects or serialized callback
state. The callback remains process-local gateway state. A remote notification
starts by resolving the `client-session` route, dispatches a command to the
gateway node's cluster endpoint, and only the gateway process performs local
callback lookup.

The remote command contains the target session key and generation, callback
contract identity, callback method identity, and serialized argument payloads.
The remote dispatcher API must not require passing `Action<TCallback>` across
the cluster boundary; that delegate is only a local capture shape used before
the command is sent.

If reliable push is enabled, the command also carries generic RPC push metadata
for the gateway to attach to the outgoing push frame. The cluster command
schema, serializer-specific formatter schema, tests, and package versions must
change together when this command shape changes.

The production business entry point is `IClientNotifications` on the business
node. Internally, the framework uses `ClusterClientNotificationDispatcher` over
the route's cluster endpoint, and `ClientNotificationCommandBinder` plus
`LocalClientNotificationCommandDispatcher` inside the gateway process. Missing
routes and stale generations return `RouteNotFound`, missing gateway callbacks
return `CallbackUnavailable`, and transport failures return `Failed`.

V1 API shape:

```csharp
await clientNotifications
    .ForSession(sessionKey)
    .NotifyAsync(notification, cancellationToken);
```

The caller expresses business intent: notify this session. The framework owns
route lookup, gateway delivery, local callback lookup, timeout handling, and
transport failure mapping.

Session routes must include session generation rather than only player id. This
avoids delivering messages to a stale connection after reconnect or multi-device
login.

For reliable notifications, business code uses the same session notification
API and the framework owns the durable or replayable reliable push protocol
state. The gateway acts as the transport relay:

```txt
business owner
  -> publishes a session notification
framework reliable push
  -> assigns sequence and records pending state when enabled
  -> sends to client-session route
gateway node
  -> delivers to local callback
client
  -> acknowledges through gateway
gateway/framework
  -> records ack and handles replay state
```

This keeps ownership explicit:

- The gateway owns transport sessions and callback objects.
- The business node owns the decision to notify.
- The framework owns ack, replay, pending limits, and reliable push storage
  when reliable push is enabled.
- The cluster owns delivery between the business node and the gateway node.

When reliable push is disabled, the same business publish path degrades to
best-effort immediate callback delivery with no ack and no replay. Business RPC
contracts should not expose reliable-push ack methods; ack is a framework
protocol negotiated during game handshake.

Feature discovery is not a load balancer. Sample or product code that allocates
rooms may use `IClusterNodeDiscovery.ListAsync` to apply deterministic
placement policy such as hashing a room, match, or player batch key across
ready `battle-runtime` candidates. Do not parse node identity from environment
variables, process ids, machine names, or advertised endpoint strings in
hotfix services, and do not use `AnyAsync` when a stable product placement
decision is required.

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

Business-facing generated APIs must throw typed exceptions instead of requiring
every caller to switch over status codes. Lower-level framework APIs may expose
status-returning calls for routing, diagnostics, and retry policy.

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
  state-store
  matchmaking
  leaderboard
endpoints:
  cluster tcp://10.0.0.1:21001
```

The data node owns state-store, matchmaking, leaderboard work, and the shared
cluster directories. The framework registers the SQL-backed `INodeDirectory`
from `Lakona:Cluster:Directory` and an explicit local in-memory
`IRouteDirectory`; gateway and battle nodes use seeded clients to reach those
directories.

Configuration:

```json
{
  "Lakona": {
    "Node": {
      "Id": "data-1"
    },
    "Feature": [ "state-store", "matchmaking", "leaderboard" ],
    "Cluster": {
      "Endpoint": "tcp://10.0.0.1:21001",
      "Serializer": "memorypack",
      "Seeds": [ "tcp://10.0.0.1:21001" ],
      "Directory": {
        "Provider": "postgres",
        "ConnectionStringName": "LakonaClusterPostgres",
        "NodeTable": "lakona_cluster_nodes",
        "EnsureSchemaOnStartup": false
      }
    }
  },
  "Agar": {
    "Persistence": {
      "Provider": "postgres",
      "ConnectionStringName": "AgarGamePostgres"
    }
  }
}
```

`Lakona:Cluster:Directory` is framework cluster membership storage.
`Agar:Persistence` is project-owned gameplay persistence configuration. The
route directory is intentionally in-memory for sample V1 but is process-local
to `data-1`, not a seeded client pointing back to `data-1`. Full durable
gameplay-state persistence remains project-owned sample work.

The sample's local Docker Postgres initializes `lakona_cluster_nodes` from
`infra/postgres/init/001-lakona-cluster-nodes.sql`; the data-node app verifies
that schema on startup. Runtime schema creation is disabled by default and can
be enabled only with `Lakona:Cluster:Directory:EnsureSchemaOnStartup=true` for
development or admin bootstrap runs.

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

Configuration:

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
        "Port": 20000,
        "Path": "/ws",
        "AdvertisedHost": "game.example.com",
        "RpcServices": [ "login", "player" ]
      }
    ],
    "Cluster": {
      "Endpoint": "tcp://10.0.0.2:21002",
      "Serializer": "memorypack",
      "Seeds": [ "tcp://10.0.0.1:21001" ]
    }
  }
}
```

`Feature: []` is valid. It means the node exposes transport endpoints and RPC
services but starts no application features.

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

Configuration:

```json
{
  "Lakona": {
    "Node": {
      "Id": "battle-1"
    },
    "Feature": [ "battle-runtime" ],
    "Endpoints": [
      {
        "Transport": "kcp",
        "Serializer": "memorypack",
        "Host": "0.0.0.0",
        "Port": 20001,
        "AdvertisedHost": "battle.example.com",
        "RpcServices": [ "battle" ]
      }
    ],
    "Cluster": {
      "Endpoint": "tcp://10.0.0.3:21003",
      "Serializer": "memorypack",
      "Seeds": [ "tcp://10.0.0.1:21001" ]
    }
  }
}
```

## Agar Data Flow

Login:

```txt
Unity client
  -> gateway-1 websocket login RPC
  -> gateway handler calls data-1 state-store through message bus or remote actor
  -> gateway stores control callback locally
  -> gateway registers client-session route for GameSessionKey
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

Remote player notification:

```txt
PlayerActor on data-1
  -> creates a profile or reward notification
  -> sends notification to client-session:<playerId>/<sessionId>/<generation>
  -> cluster routes message to gateway-1
  -> gateway-1 looks up the local WebSocket callback
  -> gateway-1 invokes callback.OnPlayerNotification(...)
```

This is the teaching example for notifications from remote business state to a
client connected through a pure WebSocket gateway. The player actor never holds
the gateway callback object and does not know the WebSocket endpoint address.

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

The local automated acceptance entry point for this topology is
`scripts/game/ci/test-agar-three-node.ps1`. It is intentionally a local
developer test because it requires Docker plus a Unity installation; it is not
part of the default repository CI gate. The test drives the existing Unity
client PlayMode flow, not a replacement console client, so it covers the
client's login, matchmaking, KCP realtime attach, and world-state observation
paths against the real three-node server topology.

## Agar Acceptance Criteria

- Three independent processes start with `Lakona` configuration.
- Node directory can see `data-1`, `gateway-1`, and `battle-1`.
- Node directory can discover `state-store`, `matchmaking`, and `leaderboard`
  on `data-1`.
- Node directory can discover `battle-runtime` on `battle-1`.
- `gateway-1` exposes only WebSocket `login` and `player` RPC services.
- `battle-1` exposes only KCP `battle` RPC service.
- A Unity client logs in through `gateway-1`.
- Login registers a client-session route owned by `gateway-1`.
- Matchmaking allocates a room on `battle-1`.
- The client receives the battle KCP endpoint and attaches to `battle-1`.
- A player actor on `data-1` can send a notification through the client-session
  route and have `gateway-1` deliver it over the local WebSocket callback.
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
