# Lakona.Game Configuration And Startup Model

## Purpose

Lakona.Game needs one long-term configuration and startup contract for generated projects, repository samples, and user applications.

The current project is still early, so this design intentionally optimizes for the long-term contract while documenting current implementation escape hatches where they exist. Incorrect configuration should fail at startup or in readiness checks; it should not be accepted through compatibility branches that make the model harder to explain.

## Design Goals

- Keep configuration small, explicit, and centered on values users actually change.
- Use one schema for generated projects and samples.
- Let projects define business Feature names without forcing framework-owned service categories.
- Let the framework own endpoint parsing, transport validation, endpoint hosting, Feature selection, dependency checks, and startup ordering.
- Let user code own business initialization logic and declared Feature dependencies.
- Avoid configuration sections for capabilities that are not implemented yet.

## Configuration Schema

All application configuration lives under `Lakona.Game`.

Supported top-level keys under `Lakona.Game`:

- `Node`: required node identity.
- `Endpoints`: optional array of business-facing RPC endpoints.
- `Feature`: optional array of project Feature names enabled in the current process.
- `Cluster`: optional compact cluster participation settings.

Generated Docker Compose scaffolds also accepts a top-level `Cluster` section for cluster operational settings such as advertised endpoints, node-directory bootstrap endpoints, node-directory storage mode, service descriptors, route lease duration, and send timeout. That section is the current framework-owned operational shape used by `ClusterOptions`; it should stay out of the default `appsettings.json` unless the project is explicitly configuring cluster deployment behavior.

The schema does not include:

- `Deployment`
- `Profile`
- `Services`
- `Services.Enabled`
- `Endpoint` as a single object
- top-level business endpoint sections such as `ControlPlane` or `Realtime`
- duplicate cluster configuration sections that represent the same setting in two places

### Single-Process Development

Omitting `Feature` means all registered project Features are enabled. Omitting `Cluster` means the process does not participate in multi-process cluster routing.

```json
{
  "Lakona.Game": {
    "Node": {
      "Id": "dev-1"
    },
    "Endpoints": [
      {
        "Transport": "websocket",
        "Host": "127.0.0.1",
        "Port": 20000,
        "Path": "/ws"
      },
      {
        "Transport": "kcp",
        "Host": "127.0.0.1",
        "Port": 20001
      }
    ]
  }
}
```

### Split Node: Login, Matchmaking, And Character Upgrade

```json
{
  "Lakona.Game": {
    "Node": {
      "Id": "game-a"
    },
    "Endpoints": [
      {
        "Transport": "websocket",
        "Host": "0.0.0.0",
        "Port": 20000,
        "Path": "/ws",
        "AdvertisedHost": "10.0.0.1"
      }
    ],
    "Feature": [
      "login",
      "matchmaking",
      "character-upgrade"
    ],
    "Cluster": {
      "Endpoint": "tcp://10.0.0.1:21001",
      "Seeds": [
        "tcp://10.0.0.1:21001"
      ]
    }
  }
}
```

### Split Node: Chat

```json
{
  "Lakona.Game": {
    "Node": {
      "Id": "game-b"
    },
    "Feature": [
      "chat"
    ],
    "Cluster": {
      "Endpoint": "tcp://10.0.0.2:21002",
      "Seeds": [
        "tcp://10.0.0.1:21001"
      ]
    }
  }
}
```

### Split Node: Battle And Realtime

```json
{
  "Lakona.Game": {
    "Node": {
      "Id": "game-c"
    },
    "Endpoints": [
      {
        "Transport": "kcp",
        "Host": "0.0.0.0",
        "Port": 20001,
        "AdvertisedHost": "10.0.0.3"
      }
    ],
    "Feature": [
      "battle",
      "battle-settlement"
    ],
    "Cluster": {
      "Endpoint": "tcp://10.0.0.3:21003",
      "Seeds": [
        "tcp://10.0.0.1:21001"
      ]
    }
  }
}
```

## Configuration Semantics

### Node

`Lakona.Game:Node:Id` is required. It identifies the current process as a Lakona.Game runtime node and appears in diagnostics. When `Cluster` is configured, the same id is used as the cluster node id.

### Endpoints

`Lakona.Game:Endpoints` is always an array. A project with one endpoint still writes a one-element array. A process with no business-facing endpoint may omit `Endpoints`.

Endpoint names are not part of the schema. The framework distinguishes endpoints by `Transport`, and the first version disallows duplicate transports in the same process.

Required endpoint fields:

- `Transport`
- `Host`
- `Port`

Optional endpoint fields:

- `Path`, only for WebSocket transports
- `AdvertisedHost`, when the externally reachable host differs from `Host`

Transport-specific rules:

- `websocket` requires `Path`.
- `kcp` must not set `Path`.
- A process cannot configure two endpoints with the same `Transport`.
- A process cannot bind two endpoints to the same host and port.

### Feature

`Lakona.Game:Feature` is an optional array of project-defined Feature names.

Rules:

- Omitted `Feature` means all Features registered in the Program.cs Feature Catalog are enabled.
- An empty array means no business Features are enabled.
- Unknown Feature names fail validation.
- Duplicate Feature names fail validation.
- The order in the array does not control startup order.

The singular key `Feature` is intentional because it names the framework concept, not a collection type.

### Cluster

`Lakona.Game:Cluster` is optional. The long-term source configuration shape is deliberately small:

```json
{
  "Endpoint": "tcp://10.0.0.1:21001",
  "Seeds": [
    "tcp://10.0.0.1:21001"
  ]
}
```

Rules:

- If `Cluster` is absent, the process does not participate in cluster routing.
- If `Cluster` is present, `Cluster.Endpoint` is required.
- If `Seeds` is omitted or empty, it defaults to the local `Cluster.Endpoint`.
- `Cluster.Endpoint` must not conflict with business endpoint ports in the same process.

The current generated Compose and health-check path uses the framework `ClusterOptions` family for operational overrides. Those settings are expressed under top-level `Cluster` environment variables:

- `Cluster:NodeId`
- `Cluster:AdvertisedEndpoints`
- `Cluster:Bootstrap:NodeDirectoryEndpoints`
- `Cluster:NodeDirectory:Enabled`
- `Cluster:NodeDirectory:Storage:Mode`
- `Cluster:Services`
- `Cluster:RouteLeaseSeconds`
- `Cluster:SendTimeoutMilliseconds`

Default local `appsettings.json` should still stay compact. Put the operational `Cluster` section in deployment-specific files or environment variables, not in the first-run config.

## Program.cs Startup Model

User code should not branch directly on raw configuration:

```csharp
if (Features.Contains("chat"))
{
    // Initialize chat.
}
```

That shape scatters configuration strings through startup code, hides ordering in imperative branches, and prevents the framework from validating Feature and endpoint dependencies before initialization.

Instead, `Program.cs` declares a Feature Catalog. The catalog defines all known project Features, their implementation types, ordering constraints, and framework requirements.

```csharp
var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddLakonaGame(builder.Configuration, game =>
{
    game.Feature<LoginFeature>("login");

    game.Feature<MatchmakingFeature>("matchmaking")
        .After("login")
        .RequiresCluster();

    game.Feature<CharacterUpgradeFeature>("character-upgrade")
        .After("login");

    game.Feature<ChatFeature>("chat")
        .RequiresCluster();

    game.Feature<BattleFeature>("battle")
        .After("matchmaking")
        .RequiresTransport("kcp")
        .RequiresCluster();

    game.Feature<BattleSettlementFeature>("battle-settlement")
        .After("battle")
        .RequiresFeature("battle");
});
```

The framework resolves the active Feature set from configuration, validates the full set, sorts it, and then calls Feature initialization in resolved order.

## Feature Implementation Model

Features should initialize business services through a framework-provided context. They should not directly parse raw `IConfiguration` to decide whether they are enabled, and they should not create RPC acceptors for endpoint transports.

Example:

```csharp
public sealed class BattleFeature : LakonaGameFeature
{
    public override void ConfigureServices(LakonaGameFeatureContext context)
    {
        var realtime = context.Endpoints.RequireTransport("kcp");
        context.Services.AddSingleton<BattleRuntime>();
    }
}
```

The context exposes resolved configuration:

- `Services`: the DI service collection.
- `Configuration`: raw configuration for business-specific settings, not Feature selection.
- `Endpoints`: framework-resolved endpoint catalog.

## Framework Responsibilities

The framework owns:

- Parsing `Lakona.Game` configuration into a resolved runtime model.
- Validating node identity.
- Validating endpoint shape, transport support, port conflicts, and transport-specific rules.
- Creating endpoint acceptors and RPC server hosting infrastructure.
- Building the Feature Catalog from `Program.cs`.
- Resolving the active Feature set from `Lakona.Game:Feature`.
- Validating Feature names, duplicates, declared dependencies, endpoint requirements, and cluster requirements.
- Sorting active Features by declared ordering and dependency constraints.
- Failing startup before listeners begin when validation has errors.
- Producing the same validation result from `--lakona-game-check`.

User code owns:

- Project Feature names.
- Business Feature implementation.
- Business service registration.
- Business-specific configuration read by a Feature after the framework has selected it.
- Declaring Feature ordering and dependencies.

## Validation Rules

Initial validation should cover only what the current framework can enforce locally.

Node validation:

- `Node.Id` is required.

Endpoint validation:

- `Endpoints`, when present, must be an array.
- Each endpoint must define `Transport`, `Host`, and a valid positive `Port`.
- Endpoint transports are case-insensitive but normalized.
- Duplicate transports in one process are errors.
- Duplicate bind ports in one process are errors.
- `websocket` endpoints require `Path`.
- `kcp` endpoints must not set `Path`.

Cluster validation:

- If `Cluster` is present, `Cluster.Endpoint` is required.
- `Cluster.Endpoint` must be a supported endpoint URI.
- `Cluster.Endpoint` must not conflict with business endpoint ports.
- Empty or missing `Seeds` resolves to the local `Cluster.Endpoint`.

Feature validation:

- Every configured Feature name must exist in the catalog.
- Duplicate configured Feature names are errors.
- If a Feature declares `.RequiresFeature("x")`, `x` must be active.
- If a Feature declares `.After("x")` and `x` is active, it must start after `x`.
- Missing or cyclic ordering dependencies are errors.
- If a Feature declares `.RequiresTransport("kcp")`, a matching endpoint must exist in the current process.
- If a Feature declares `.RequiresCluster()`, `Cluster` must be configured.

Out-of-scope validation:

- Whole-deployment validation across multiple config files.
- Production profile checks.
- Provider-specific durable node-directory behavior beyond validating selected local options.
- Multi-instance same-transport endpoints.

## Check Command

Readiness checks should call the same resolver and validator used by startup. `--lakona-game-check` remains a compatibility alias for local project inspection; new generated deployment checks should prefer `--readiness-check`, and Docker liveness should use `--health-check`.

Suggested text output:

```txt
node: ok game-c
endpoints: ok kcp://0.0.0.0:20001
cluster: ok tcp://10.0.0.3:21003 seeds=1
feature: ok battle, battle-settlement
startup-order: ok battle -> battle-settlement
```

When validation fails, diagnostics must name the exact path and repair:

```txt
ULINK023 error Lakona.Game:Endpoints[0]: websocket endpoint requires Path.
fix: set Lakona.Game:Endpoints:0:Path to a path such as /ws
```

## Sample And Tool Direction

Generated projects and repository samples should use the same schema.

The tool should generate:

- `Lakona.Game:Node`
- `Lakona.Game:Endpoints`
- optional `Lakona.Game:Feature` only when the generated project is intentionally split
- optional `Lakona.Game:Cluster` only when the selected template participates in cluster routing
- top-level operational `Cluster` environment variables only for deployment scaffolds such as Docker Compose

`samples/Agar.Unity` should move its control WebSocket and realtime KCP listeners into `Lakona.Game:Endpoints`. `control` and `realtime` remain business concepts in Agar code, not endpoint names in the framework schema. Agar Features can declare transport requirements, for example battle requiring `kcp` and login/control requiring `websocket`.

Because the framework is early, legacy shapes do not need to be supported:

- no top-level `ControlPlane`
- no top-level `Realtime`
- no `Lakona.Game:Endpoint` single object
- no `Lakona.Game:Services`
- no `Deployment`

## Risks And Tradeoffs

Disallowing duplicate transports keeps the first endpoint model simple but prevents advanced cases such as two WebSocket listeners in one process. That is acceptable for the first long-term contract; a future design can add optional endpoint `Name` only when a real project needs same-transport multi-listener support.

Using `Feature` instead of `Services` keeps the schema aligned with the current framework vocabulary, but it requires the Feature Catalog to be precise. The framework should make Feature registration errors obvious.

Keeping deployment-level validation out of scope limits what the framework can catch before a multi-node deployment starts. That is intentional until the local runtime model is stable.

## Acceptance Criteria

- The canonical docs and generated templates use `Lakona.Game:Endpoints` as an array.
- Generated projects do not emit unsupported configuration sections.
- Runtime startup and `--lakona-game-check` use the same resolver and validator.
- Users declare Feature order and dependencies in code, not by writing manual `if` branches over raw configuration.
- Endpoint transport hosting is framework-owned.
- A sample with WebSocket control traffic and KCP realtime traffic can be represented without endpoint names in JSON.
