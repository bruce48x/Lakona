# Configuration

## Purpose

Lakona uses one runtime configuration root for generated projects, repository
samples, and user applications: `Lakona`.

This document is the compact startup contract. The fuller distributed runtime
model is documented in [cluster.md](cluster.md).

## Configuration Schema

Supported top-level keys under `Lakona`:

- `Node`: required node identity.
- `Feature`: optional array selecting discovered `LakonaGameFeature` names.
- `Endpoints`: optional array of client-facing RPC listeners.
- `Cluster`: optional node-to-node cluster settings.

The legacy `Lakona.Game` root is only a compatibility read path for old
applications and explicit compatibility tests. New samples, generated projects,
docs, and diagnostics must use `Lakona`.

### Minimal Generated App

```json
{
  "Lakona": {
    "Node": {
      "Id": "dev-1"
    },
    "Endpoints": [
      {
        "Transport": "kcp",
        "Serializer": "memorypack",
        "Host": "127.0.0.1",
        "Port": 20000,
        "RpcServices": [ "login", "chat" ]
      }
    ]
  }
}
```

For WebSocket transport, include `Path`:

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
      "Seeds": [ "tcp://10.0.0.1:21001" ]
    }
  }
}
```

## Feature Startup

Stable `LakonaGameFeature` is framework infrastructure for process startup.
User-authored game feature declarations live in the hotfix assembly as
`HotfixGameFeature` descriptors.

The previous role/filter model and older hand-written fluent catalog are
superseded for generated projects and new samples. Do not use role-shaped
configuration or endpoint names for new startup code.

Generated projects should use convention-based registration:

```csharp
builder.Services.AddLakonaGame(builder.Configuration);
```

Hotfix descriptors declare reloadable actor runtime loops:

```csharp
[HotfixFeature("battle-runtime")]
public sealed class BattleRuntimeFeature : HotfixGameFeature
{
    public override void Configure(HotfixFeatureContext context)
    {
        context.ScheduleActorTick<MatchmakingActor>(
            "default",
            TimeSpan.FromMilliseconds(250),
            TickBacklogPolicy.Coalesce);

        context.ScheduleActiveActorTicks<RoomActor>(
            TimeSpan.FromMilliseconds(50),
            TickBacklogPolicy.SkipIfPending);
    }
}
```

`Lakona:Feature` controls activation:

- omitted `Feature` enables all discovered features;
- `Feature: []` enables no business features;
- unknown or duplicate names fail startup.

Business concepts such as `matchmaking` or `battle-runtime` are feature names,
not endpoint names. RPC service exposure is configured per endpoint through
`RpcServices`.

Features may opt out of cluster publication with
`public override bool Discoverable => false`.

Feature selection is stable process topology. `Lakona:Feature` decides which
startup adapters and cluster capabilities this process owns. It does not select
which hotfix business rules are loaded. Hotfix services and actor behaviors are
loaded by the hotfix manager after the stable host shape is known.

The descriptor may declare actor ticks and other reloadable game capabilities.
It must not decide matchmaking batches, room results, leaderboard ranks, login
policy, presence cleanup, or product DTO projection directly; those decisions
belong in hotfix services and actor behaviors.

## Endpoint Rules

`Lakona:Endpoints` entries are listener configuration and RPC service exposure.
They do not create session identities and they do not name endpoints.

Rules:

- `Transport`, `Serializer`, `Host`, and `Port` are required for each endpoint.
- Supported serializers are `json` and `memorypack`.
- WebSocket endpoints require `Path`.
- KCP endpoints must not set `Path`.
- A process cannot configure duplicate transports.
- `RpcServices` is endpoint-local; duplicate service names in one endpoint fail.
- Unknown `RpcServices` fail because no `LakonaRpcServiceBinder` is registered.
- Endpoint `Name` is not part of the V1 schema.

RPC service binding is independent from Feature selection. The RPC handler owns
business dispatch to local services, feature-addressed messages, actors, or
other project code.

## Cluster Rules

`Lakona:Cluster` is node-to-node configuration:

```json
{
  "Lakona": {
    "Cluster": {
      "Endpoint": "tcp://10.0.0.1:21001",
      "Seeds": [ "tcp://10.0.0.1:21001" ]
    }
  }
}
```

The cluster endpoint is not listed in `Endpoints`; it is advertised separately
as the `cluster` endpoint for node-to-node traffic.

`Seeds` is the public bootstrap list for shared cluster directories. A data
node can register local node-directory and route-directory implementations,
while gateway and battle nodes use the seed endpoint to register node leases,
write client-session routes, and resolve those routes through remote directory
clients.

Do not generate or document `Lakona:Cluster:Services`. Cluster discovery uses
node features (`NodeFeatureDescriptor`) and advertised endpoints.

## Validation

Startup and readiness checks use the same runtime resolver. Invalid
configuration should fail before listeners begin accepting traffic.

Validation covers:

- node identity;
- endpoint shape, serializer selection, duplicate transports, and transport-specific rules;
- endpoint-local `RpcServices`;
- feature names, duplicates, and dependency/order constraints;
- cluster endpoint and seed shape when cluster is configured.

`--lakona-game-check` remains a compatibility alias for local project
inspection. New deployment automation should prefer `--readiness-check` and
`--health-check`.

Example diagnostic wording:

```txt
ULINK023 error Lakona:Endpoints[0]: websocket endpoint requires Path.
fix: set Lakona:Endpoints:0:Path to a path such as /ws
```

## Generated Project Direction

`Lakona.Tool` generated projects should emit:

- `Lakona:Node`;
- `Lakona:Endpoints[]` with endpoint-local `Serializer` and `RpcServices`;
- optional `Lakona:Feature` only when the generated project is intentionally
  split;
- optional `Lakona:Cluster` only when the selected template participates in
  cluster routing.

Generated projects should not emit service endpoint marker files, endpoint
`Name`, hidden `control` or `realtime` endpoint names, `Cluster.Directory`, or
`Services` lists.
