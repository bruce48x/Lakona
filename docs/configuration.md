# Configuration

## Purpose

Lakona uses one runtime configuration root for generated projects, repository
samples, and user applications: `Lakona`.

This document is the compact startup and configuration contract. It covers the
runtime schema, configuration provider order, production package selection,
environment-variable overrides, Docker Compose sample shape, JSON array
binding, and validation boundary. The fuller distributed runtime model is
documented in [cluster.md](cluster.md).

## Configuration Schema

Supported top-level keys under `Lakona`:

- `Profile`: optional runtime profile override. Supported values are
  `Development`, `Compose`, and `Production`.
- `Node`: required node identity.
- `Feature`: optional array selecting discovered `LakonaGameFeature` names.
- `Endpoints`: optional array of client-facing RPC listeners.
- `Cluster`: optional node-to-node cluster settings.
- `Sessions`: optional framework session cleanup and retention settings.
- `ReliablePush`: optional reliable push settings.
- `Observability`: optional framework logging, local admin, framework
  diagnostics, metrics, and tracing settings.

The legacy `Lakona.Game` root is obsolete and is not read by the current
runtime. Samples, generated projects, docs, diagnostics, and deployments must
use `Lakona`.

## Configuration Sources

Lakona server processes must be configurable from deployment configuration
files and environment variables without changing application binaries.

`LakonaGameServer.RunAsync` uses the default .NET configuration provider order
with the application base directory as the content root:

```txt
appsettings.json
appsettings.{Environment}.json
user secrets, when applicable
environment variables
command line
```

The host must not append `appsettings.json` after environment variables.
Appending JSON late lets packaged or container-baked files override deployment
configuration and breaks the expected .NET precedence model.

Environment variables override both JSON files. Use them for secrets,
host-specific overrides, and values supplied by deployment automation.

## Production Package Configuration

Production deployment uses `lakona-tool server pack`, not Docker images as the
primary release artifact. The package is a normal application root after
extraction:

```txt
Server.App.dll
appsettings.json
appsettings.battle-1.json     # optional deployment-provided node config
lakona-server.json
hotfix/
```

The environment name selects the node-specific JSON file:

```bash
DOTNET_ENVIRONMENT=battle-1 dotnet Server.App.dll
```

With that environment variable, the default host reads:

```txt
appsettings.json
appsettings.battle-1.json
environment variables
command line
```

The node-specific file must be in the application content root, which is the
extracted server package directory. Operators may place
`appsettings.battle-1.json` there during deployment, or package it from their
deployment project if that project intentionally owns environment-specific
files.

## Environment Variables

Environment variables use .NET's standard double-underscore hierarchy syntax:

| Environment variable | Configuration key |
| --- | --- |
| `Lakona__Node__Id` | `Lakona:Node:Id` |
| `Lakona__Cluster__Endpoint` | `Lakona:Cluster:Endpoint` |
| `Lakona__Cluster__Directory__Provider` | `Lakona:Cluster:Directory:Provider` |
| `Agar__Persistence__Provider` | `Agar:Persistence:Provider` |

Single underscore is ordinary text and must not be treated as hierarchy.

### Array Values

.NET supports arrays with numeric keys such as `Lakona__Feature__0`, but that
shape is noisy for deployment files and easy to misread. Lakona also supports
JSON string values for array-shaped configuration sections.

The framework accepts these equivalent forms:

```txt
Lakona__Feature__0=state-store
Lakona__Feature__1=matchmaking
Lakona__Feature__2=leaderboard
```

```txt
Lakona__Feature=["state-store","matchmaking","leaderboard"]
```

The JSON string form is the recommended environment-variable style for arrays.
The indexed form remains valid because it is native .NET configuration.

An empty JSON array is explicit and meaningful:

```txt
Lakona__Feature=[]
Lakona__Endpoints=[]
```

For `Feature`, omitted and empty are different:

- omitted `Lakona:Feature` enables all discovered features;
- `Lakona__Feature=[]` enables no application features.

### Complex Lists

`Lakona:Endpoints` is a complex list and can be expressed as one JSON string:

```yaml
environment:
  Lakona__Node__Id: gateway-1
  Lakona__Endpoints: >-
    [
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
```

The framework parses endpoint JSON case-insensitively and binds it to the same
`LakonaGameEndpointOptions` model used by JSON files.

Indexed endpoint configuration remains valid:

```txt
Lakona__Endpoints__0__Transport=websocket
Lakona__Endpoints__0__Serializer=memorypack
Lakona__Endpoints__0__Host=0.0.0.0
Lakona__Endpoints__0__Port=20000
Lakona__Endpoints__0__Path=/ws
Lakona__Endpoints__0__RpcServices__0=login
Lakona__Endpoints__0__RpcServices__1=player
```

The JSON string form is preferred for human-authored deployment manifests.

### Cluster Seeds

`Lakona:Cluster:Seeds` supports both native indexed keys and a JSON string
array.

Preferred:

```yaml
environment:
  Lakona__Cluster__Endpoint: tcp://10.0.0.2:21002
  Lakona__Cluster__Serializer: memorypack
  Lakona__Cluster__Seeds: '["tcp://10.0.0.1:21001"]'
```

Also valid:

```txt
Lakona__Cluster__Seeds__0=tcp://10.0.0.1:21001
```

## Docker Compose Configuration

Docker Compose is the local Agar sample and E2E topology, not the primary
production release flow. Production should use `lakona-tool server pack` plus
deployment-managed configuration files and environment variables.

For the Agar three-node sample, the final Docker image should remove
`appsettings*.json` and let `docker-compose.yml` provide the full node runtime
configuration through environment variables.

Data node:

```yaml
environment:
  DOTNET_ENVIRONMENT: data-1
  Lakona__Node__Id: data-1
  Lakona__Feature: '["state-store","matchmaking","leaderboard"]'
  Lakona__Cluster__Endpoint: tcp://10.0.0.1:21001
  Lakona__Cluster__Serializer: memorypack
  Lakona__Cluster__Seeds: '["tcp://10.0.0.1:21001"]'
  Lakona__Cluster__Directory__Provider: postgres
  Lakona__Cluster__Directory__ConnectionStringName: LakonaClusterPostgres
  Lakona__Cluster__Directory__NodeTable: lakona_cluster_nodes
  Lakona__Cluster__Directory__EnsureSchemaOnStartup: "false"
  Agar__Persistence__Provider: postgres
  Agar__Persistence__ConnectionStringName: AgarGamePostgres
```

Gateway node:

```yaml
environment:
  DOTNET_ENVIRONMENT: gateway-1
  Lakona__Node__Id: gateway-1
  Lakona__Feature: "[]"
  Lakona__Endpoints: >-
    [
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
  Lakona__Cluster__Endpoint: tcp://10.0.0.2:21002
  Lakona__Cluster__Serializer: memorypack
  Lakona__Cluster__Seeds: '["tcp://10.0.0.1:21001"]'
```

Battle node:

```yaml
environment:
  DOTNET_ENVIRONMENT: battle-1
  Lakona__Node__Id: battle-1
  Lakona__Feature: '["battle-runtime"]'
  Lakona__Endpoints: >-
    [
      {
        "Transport": "kcp",
        "Serializer": "memorypack",
        "Host": "0.0.0.0",
        "AdvertisedHost": "battle-1",
        "Port": 20001,
        "RpcServices": [ "battle" ]
      }
    ]
  Lakona__Cluster__Endpoint: tcp://10.0.0.3:21003
  Lakona__Cluster__Serializer: memorypack
  Lakona__Cluster__Seeds: '["tcp://10.0.0.1:21001"]'
```

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
      "Serializer": "json",
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

Generated projects use a strict zero-template host:

```csharp
using Lakona.Game.Server.Hosting;

return await LakonaGameServer.RunAsync(args);
```

Generated and sample `Program.cs` files must not hand-write framework
registration calls such as:

```txt
AddMessageRecording()
AddLakonaGameRuntimeValidation()
AddLakonaGame(...)
UseGeneratedHotfixServices()
```

`LakonaGameServer.RunAsync` owns framework defaults, generated binder
discovery, required hotfix contract discovery, validation, message recording,
session lifecycle bridges, reliable push defaults, cluster startup, and
endpoint listener startup. Application behavior belongs in configuration,
shared contracts, stable actor state shells, and `Server.Hotfix`.

Hotfix descriptors declare local actors and lifecycle hooks. Periodic work uses
the framework-owned timer API from feature lifecycle methods:

```csharp
[HotfixFeature("battle-runtime")]
public sealed class BattleRuntimeFeature : HotfixGameFeature
{
    public static void Configure(HotfixFeatureContext context)
    {
        context.EnsureLocalActor<MatchmakingActor>("default");
    }

    public static async ValueTask StartAsync(HotfixFeatureStartCall call)
    {
        await LakonaTimer.CreatePeriodicTimerAsync<BattleRuntimeTimers, BattleRuntimeTick>(
            TimeSpan.Zero,
            TimeSpan.FromMilliseconds(50),
            nameof(BattleRuntimeTimers.TickAsync),
            new BattleRuntimeTick("default"),
            call.CancellationToken);
    }
}

public sealed record BattleRuntimeTick(string QueueId);
```

`Lakona:Feature` controls activation:

- omitted `Feature` enables all discovered features;
- `Feature: []` enables no business features;
- unknown or duplicate names fail startup.

Business concepts such as `matchmaking` or `battle-runtime` are feature names,
not endpoint names. RPC service exposure is configured per endpoint through
`RpcServices`.

Features may opt out of cluster publication from static `Configure`:

```csharp
public static void Configure(HotfixFeatureContext context)
{
    context.Discoverable = false;
}
```

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

Endpoint `Serializer` selects the client-facing business RPC payload
serializer for that listener. It does not select the node-to-node cluster
serializer and it does not select the Lakona.Game framework-internal control
codec.

Node-to-node cluster traffic uses `Lakona:Cluster:Serializer`. Remote actor
request and reply payloads follow the cluster serializer because remote actor
calls travel over the cluster channel. There is no second user-facing actor
serialization setting. Framework-internal client control messages such as
handshake, heartbeat, reliable push ack, and session termination notice use
`LakonaInternalCodec` on every endpoint.

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

Generated projects and ordinary hotfix business code should not register
hotfix-side `IFeatureMessageHandler` implementations. The default stable cluster
endpoint owns the low-level handler and dispatches typed commands into the
current hotfix feature command table. Advanced hosts may replace the stable
`IFeatureMessageHandler`, but that replacement owns the whole low-level feature
message surface.

## Reliable Push

Reliable push is enabled by default as part of the Lakona game runtime. The
default generated `appsettings.json` usually omits the section because the
framework derives the default.

Explicit configuration may opt out:

```json
{
  "Lakona": {
    "ReliablePush": {
      "Enabled": false
    }
  }
}
```

`Enabled: false` does not remove the notification API. It changes delivery to
immediate best-effort callback delivery with no ack and no replay. The resolved
mode is sent to clients during the framework game handshake.

## Observability

Lakona emits logs, metrics, and traces through the standard .NET diagnostics
stack: `ILogger`, `Meter`, and `ActivitySource`. Framework defaults live under
`Lakona:Observability`. This root controls framework log defaults, the
loopback local admin host, event buffering, diagnostics detail guardrails,
metrics endpoint exposure, and tracing export.

`Lakona:Observability:LocalAdmin:Enabled` is optional. When it is omitted, the
default comes from the resolved `Lakona:Profile`, not directly from the raw
`DOTNET_ENVIRONMENT` string:

- `Development` enables the loopback local admin host by default.
- `Compose` and `Production` disable it by default.
- any profile may explicitly set `LocalAdmin:Enabled`, but enabled local admin
  must bind to loopback unless `RequireLoopback` is intentionally disabled for a
  trusted local environment.

`Lakona:Profile` may override the host environment name. If `Lakona:Profile` is
omitted, the host maps `DOTNET_ENVIRONMENT=Development` to `Development`,
`DOTNET_ENVIRONMENT=Compose` to `Compose`, and other environment names to
`Production`.

When local admin is enabled, the framework registers safe core diagnostics
routes on the local admin host, including `/_lakona/diagnostics/summary`,
`/_lakona/diagnostics/events`, `/_lakona/diagnostics/netstat`,
`/_lakona/diagnostics/actors`, and `/_lakona/diagnostics/sessions`.
`Lakona:Observability:Diagnostics:DetailEnabled` controls whether detail
exposure passes the framework guardrails. It is not required for the safe core
summary routes.

Example:

```json
{
  "Lakona": {
    "Profile": "Development",
    "Node": {
      "Id": "dev-1"
    },
    "Observability": {
      "Logging": {
        "Enabled": true,
        "MinimumLevel": "Information",
        "Categories": {
          "Lakona.Rpc": "Information",
          "Lakona.Rpc.Transport": "Information",
          "Lakona.Game.Server": "Information",
          "Lakona.Game.Session": "Information",
          "Lakona.Game.Actor": "Information",
          "Lakona.Game.Cluster": "Information",
          "Lakona.Game.Hotfix": "Information",
          "Lakona.Game.Observability": "Information"
        },
        "Console": {
          "Enabled": true,
          "Format": "Compact",
          "IncludeScopes": false
        },
        "File": {
          "Enabled": false,
          "Path": "logs/lakona-.log",
          "RollingInterval": "Day",
          "RetainedFileCount": 7,
          "FileSizeLimitMB": 128
        }
      },
      "LocalAdmin": {
        "Enabled": true,
        "Host": "127.0.0.1",
        "Port": 20090,
        "RequireLoopback": true
      },
      "Diagnostics": {
        "SummaryEnabled": true,
        "DetailEnabled": false,
        "EventBuffer": {
          "Enabled": true,
          "Capacity": 1024,
          "MinimumLevel": "Warning"
        }
      },
      "Metrics": {
        "Prometheus": {
          "Enabled": false,
          "Path": "/_lakona/metrics"
        }
      },
      "Tracing": {
        "Export": {
          "Enabled": false,
          "SampleRate": 1.0
        }
      }
    }
  }
}
```

`SummaryEnabled` is parsed for compatibility with the observability options
shape, but in this slice the framework registers core summary diagnostics routes
whenever local admin is enabled. Treat it as a compatibility/default field, not
as a route switch.

File logging, Prometheus endpoint serving, and tracing export are integration
points. Enabling them without registering the corresponding implementation is a
validation error.

## Cluster Rules

`Lakona:Cluster` is node-to-node configuration:

```json
{
  "Lakona": {
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
  }
}
```

The cluster endpoint is not listed in `Endpoints`; it is advertised separately
as the `cluster` endpoint for node-to-node traffic.

`Lakona:Cluster:Serializer` is required whenever `Lakona:Cluster` is
configured. Supported values are `json` and `memorypack`. All communicating
cluster nodes must use the same cluster serializer; node-directory calls,
route-directory calls, feature-addressed messages, client-notification relay
commands, and remote actor payloads in cluster mode follow this setting. Keep
`Lakona:Cluster:Serializer` as the only user-facing cluster serializer switch;
do not add a separate actor serialization setting. This is separate from
endpoint-local `Lakona:Endpoints[]:Serializer`, and client-facing
framework-control messages continue to use `LakonaInternalCodec`.

`Seeds` is the public bootstrap list for shared cluster directories. A data
node can register local node-directory and route-directory implementations,
while gateway and battle nodes use the seed endpoint to register node leases,
write client-session routes, and resolve those routes through remote directory
clients.

Do not generate or document `Lakona:Cluster:Services`. Cluster discovery uses
node features (`NodeFeatureDescriptor`) and advertised endpoints.

Cluster directory database configuration lives under
`Lakona:Cluster:Directory`. Game-specific persistence lives under a separate
application-owned root such as `Agar:Persistence`.

When a database is used as the cluster node directory, it is framework
infrastructure and belongs under `Lakona:Cluster:Directory`. When a database is
used for account data, match records, leaderboards, inventories, or other
gameplay state, it is business infrastructure and belongs under an
application-owned root. Do not model database as a runtime `Feature`.

## Validation

Startup and readiness checks use the same runtime resolver. Invalid
configuration should fail before listeners begin accepting traffic.

Validation covers:

- node identity;
- endpoint shape, serializer selection, duplicate transports, and transport-specific rules;
- endpoint-local `RpcServices`;
- feature names, duplicates, and dependency/order constraints;
- cluster endpoint and seed shape when cluster is configured;
- required cluster serializer and supported values;
- observability local admin loopback safety;
- diagnostics detail mode exposure;
- file logging, Prometheus, and tracing exporter integration requirements;
- observability metrics path, event buffer capacity, log level, and trace
  sample rate.

All communicating cluster nodes must still be operated with the same cluster
serializer; startup validation only checks local presence and supported values.

Malformed JSON environment values are configuration binding failures before
semantic validation.

`--readiness-check` is the canonical project readiness command for local
inspection and deployment automation. Use `--health-check` for liveness-only
checks.

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
  cluster routing. When generated, `Lakona:Cluster:Serializer` should be set
  from the same `--serializer` choice used for generated business RPC contracts.

Generated projects should not emit service endpoint marker files, endpoint
`Name`, hidden `control` or `realtime` endpoint names, or `Services` lists.

## Implementation Requirements

`LakonaGameRuntimeOptions.FromConfiguration` must recognize JSON string values
for these sections:

- `Lakona:Feature`
- `Lakona:Endpoints`
- `Lakona:Endpoints:*:RpcServices`
- `Lakona:Cluster:Seeds`

The JSON parser must:

- accept arrays only where arrays are expected;
- fail with a clear configuration error for malformed JSON;
- bind endpoint property names case-insensitively;
- preserve the current behavior for indexed .NET configuration arrays.

Tests must cover:

- environment variables override `appsettings.json`;
- `Lakona__Feature` JSON array binds to `Feature`;
- `Lakona__Feature=[]` binds to an explicit empty array;
- `Lakona__Endpoints` JSON array binds endpoint transport, serializer, bind
  host, advertised host, port, path, and RPC services;
- `Lakona__Cluster__Seeds` JSON array binds seed endpoints;
- native indexed environment arrays still work;
- malformed JSON produces a clear error;
- `DOTNET_ENVIRONMENT=battle-1` loads `appsettings.battle-1.json` from the
  application content root in package-style deployments;
- Agar compose expresses data, gateway, and battle topology without
  node-specific `appsettings.*.json` files.
