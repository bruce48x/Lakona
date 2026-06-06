# Lakona.Game Runtime Guardrails

## Purpose

`lakona new` reduces the configuration surface for new projects, but the runtime framework must still protect projects from invalid or unsafe states after users start editing configuration, deployment profiles, or generated code.

Runtime guardrails make Lakona.Game easier to use and easier to maintain by moving common "do not configure it this way" knowledge into framework-owned validation, diagnostics, and check output.

The goal is not to remove advanced configuration. The goal is to make the default path safe, make advanced paths explicit, and fail fast when a configuration violates Lakona.Game runtime invariants.

## Design Principle

Tooling and runtime validation have different responsibilities:

- `Lakona.Game.Tool` hides unnecessary choices and generates safe defaults.
- Lakona.Game runtime packages enforce invariants that must hold for a server to run correctly.
- `--health-check`, `--readiness-check`, and the compatibility `--lakona-game-check` explain runtime state at the right operational boundary.

Do not make Cluster, Hotfix, or Reliable Push ordinary optional modules in generated projects. They are part of the Lakona.Game application model. Users may change their source, storage, topology, or deployment profile, but generated projects should not teach users to disable the core model.

## Responsibility Boundary

Use this boundary when deciding where a rule or default belongs:

| Concern | Owner |
| --- | --- |
| New project command shape and first-run text | `Lakona.Game.Tool` |
| Default runtime derivation | framework defaults |
| Compact `appsettings.json` rendering | `Lakona.Game.Tool` |
| Runtime invariants | framework validators |
| Local repair guidance | check formatter |
| Production readiness | profile validators |
| Legacy project compatibility | validators and migration warnings |
| Internal derived values | resolved runtime model |

Generated projects should call framework APIs for default derivation when possible instead of embedding framework rules in generated source. Generated source may provide project-specific presentation, but it should not become the long-term owner of Cluster, Hotfix, Reliable Push, or endpoint derivation rules.

Suggested generated host shape:

```csharp
builder.Services.AddLakonaGame(builder.Configuration, game =>
{
    game.Feature<LoginFeature>("login").RequiresTransport("websocket");
    game.Feature<BattleFeature>("battle").RequiresTransport("kcp");
});
```

This keeps generated projects aligned with the Feature Catalog startup model in [Lakona.Game Configuration And Startup Model](lakona-game-configuration-startup.md). When a default rule improves in the framework, projects should benefit by updating package versions rather than by regenerating their server host.

## Validation Levels

Runtime guardrails use three levels.

### Errors

Errors are invalid states. Startup or `--lakona-game-check` should fail.

Use errors for framework invariants:

- node id is missing or has an invalid format
- endpoint transport is unknown
- endpoint scheme, transport, and path are inconsistent
- WebSocket transport cannot derive a listener path
- endpoint transport names are duplicated where the framework needs one endpoint per transport
- cluster service names are duplicated
- cluster service kind is unknown
- gateway service is configured without reachable route-directory or node-directory support
- advertised endpoint cannot be parsed
- advertised endpoint conflicts with the configured listener in a way the runtime cannot route
- Hotfix assembly is missing or no initial hotfix assembly can be loaded
- Hotfix reload produces duplicate dispatch keys or unsupported method signatures
- Reliable Push is enabled but no session identity or resume identity resolver is available
- production profile advertises localhost or loopback endpoints
- production profile selects in-memory node directory storage

### Warnings

Warnings are states that may be acceptable in development but are risky or surprising.

Use warnings for local or temporary defaults:

- Reliable Push uses in-memory storage
- node directory uses in-memory storage
- route directory uses in-memory storage
- advertised endpoint is loopback in a development profile
- endpoint uses a default port
- single-node topology is active
- persistence is not configured
- route lease duration, send timeout, replay retention, or pending push limit uses defaults

Warnings should not make local development painful. They should be visible in `--lakona-game-check` and diagnostics.

Hotfix assembly absence is not a warning in any profile or command mode. It is always an error because Hotfix is part of the Lakona.Game default application model. `--lakona-game-check` should make the repair path friendly, but it must still return a non-zero exit code.

Normal server startup must fail when the initial Hotfix load fails. A missing, invalid, or scanner-rejected `Server.Hotfix.dll` is not a degraded mode. `--lakona-game-check`, readiness checks, and normal startup must agree that Hotfix absence is an error for generated Lakona.Game projects.

### Info

Info explains derived state without implying risk:

- selected node id
- selected transport and listener address
- derived advertised client endpoint
- configured service list
- hotfix source type and assembly name
- reliable push replay window
- selected topology or deployment profile

## Profiles

Validation should be profile-aware.

The default generated profile is development. Development allows local defaults such as single-node topology, loopback endpoints, in-memory directories, and in-memory reliable push storage.

Production-oriented profiles must be stricter. A production profile should reject configuration that is only safe for local development, including loopback advertised endpoints and in-memory cluster directory storage.

Profiles should not reintroduce `Hotfix.Enabled`, `Cluster.Enabled`, or `ReliablePush.Enabled` as normal user-facing switches. A profile changes topology, storage, endpoints, and operational strictness; it does not redefine the Lakona.Game application model.

Profiles should be represented as framework-owned values, not arbitrary strings. This keeps validation testable and avoids making users guess hidden mode names.

Suggested profile values:

- `Development`: generated default; allows single-node topology, loopback endpoints, and in-memory local stores.
- `Compose`: local multi-container rehearsal; allows private container-network endpoints but should surface external advertised endpoint warnings.
- `Production`: rejects local-only defaults and requires explicit operational choices for advertised endpoints and durable cluster directory storage.

Each profile should define:

- which defaults are allowed
- which warnings become errors
- whether loopback advertised endpoints are valid
- whether in-memory node-directory or route-directory storage is valid
- whether advertised endpoints must be explicit
- whether Reliable Push requires durable storage or only identity validation

## Resolved Runtime Model

Validation should run against a resolved runtime model, not raw configuration.

The data flow is:

```txt
raw configuration
  -> source options
  -> resolved runtime model
  -> validators
  -> startup behavior / check output
```

The resolved model should contain final values and where they came from. Provenance makes diagnostics clearer and helps users understand whether a value came from `appsettings.json`, environment variables, a profile default, or a generated project convention.

Suggested model shape:

```csharp
public enum LakonaGameValueSource
{
    Default,
    Configuration,
    Environment,
    GeneratedConvention,
    Code
}

public sealed record LakonaGameResolvedValue<T>(
    T Value,
    LakonaGameValueSource Source,
    string? Path = null);

public sealed record LakonaGameResolvedRuntime(
    LakonaGameResolvedValue<string> NodeId,
    IReadOnlyList<LakonaGameResolvedEndpoint> Endpoints,
    LakonaGameResolvedCluster Cluster,
    LakonaGameResolvedHotfix Hotfix,
    LakonaGameResolvedReliablePush ReliablePush,
    LakonaGameRuntimeProfile Profile);
```

The exact type names can change during implementation, but the boundary should remain: validators read the same resolved state that server startup will use.

## Runtime Validation API

Add a framework-owned validation model that can be reused by server startup, generated check commands, tests, and future tooling.

Suggested shape:

```csharp
public enum LakonaGameDiagnosticSeverity
{
    Info,
    Warning,
    Error
}

public sealed record LakonaGameDiagnostic(
    string Code,
    LakonaGameDiagnosticSeverity Severity,
    string Message,
    string? Repair = null);

public sealed record LakonaGameValidationResult(
    IReadOnlyList<LakonaGameDiagnostic> Diagnostics)
{
    public bool Succeeded => Diagnostics.All(diagnostic => diagnostic.Severity != LakonaGameDiagnosticSeverity.Error);
}

public interface ILakonaGameValidationRule
{
    IEnumerable<LakonaGameDiagnostic> Validate(LakonaGameResolvedRuntime runtime);
}
```

The resolved runtime model should be built before validation, and it should not expose raw JSON as the validator input. Validators should see the same final state the server will use.

Suggested validator groups:

- `NodeIdentityRules`
- `EndpointRules`
- `ClusterServiceGraphRules`
- `HotfixSourceRules`
- `ReliablePushIdentityRules`
- `ProductionReadinessRules`

Each rule should be small, deterministic, and testable. It should answer one question and should not start network listeners or mutate runtime state. A composed runtime validator can aggregate the rules and produce a single `LakonaGameValidationResult`.

## Package Boundaries

Guardrails cross several packages, so ownership must stay explicit.

Recommended package split:

- diagnostic primitives belong in the lowest package that both client-facing and server-facing tooling can reference without pulling in transport or hosting dependencies
- server hosting composes the validation pipeline and owns startup behavior
- Cluster packages provide Cluster-specific rules without depending on `Lakona.Game.Server`
- Hotfix packages provide Hotfix-specific rules without depending on `Lakona.Game.Tool`
- Reliable Push and session rules live with the server-side session/push abstractions they validate
- `Lakona.Game.Tool` calls the generated host/check surface; it does not own runtime invariants

Do not make `Lakona.Game.Cluster` depend on `Lakona.Game.Server` only to participate in validation. Prefer extension methods or rule registration hooks that allow server hosting to compose optional package rules.

## Diagnostic Codes

Diagnostics should use stable codes so documentation, tests, logs, and check output can refer to the same condition.

Initial code families:

- `ULINK001-ULINK019`: node identity and profile
- `ULINK020-ULINK039`: endpoint and advertised addresses
- `ULINK040-ULINK069`: cluster services, node directory, route directory
- `ULINK070-ULINK089`: hotfix loading and dispatch
- `ULINK090-ULINK109`: Reliable Push and session identity
- `ULINK110-ULINK129`: production readiness

Messages should be short and actionable. Repairs should tell the user what to change or what command to run.

Example diagnostics:

```txt
ULINK001 error Node id is required.
ULINK023 error WebSocket endpoint path is required.
ULINK041 error Cluster service name 'gateway' is duplicated.
ULINK071 error Hotfix assembly was not found.
ULINK071 repair dotnet build Server/Hotfix/Server.Hotfix.csproj
ULINK091 error Reliable Push requires a session identity resolver.
ULINK111 error Production profile cannot advertise 127.0.0.1.
```

## Startup Behavior

Server startup should run runtime validation after configuration has been bound and derived, but before the server starts accepting traffic.

If validation returns errors:

- log all diagnostics
- throw a single startup exception that summarizes the error count and first actionable error
- do not start listeners

If validation returns warnings:

- log warnings
- continue startup in development profile
- fail startup in production profile only when the warning represents a production readiness rule promoted to error

Startup exceptions should preserve diagnostic codes so tests and tools can assert them without string matching.

## Check Command Behavior

Generated readiness checks should call the same runtime validation pipeline used by startup. `--lakona-game-check` remains available for compatibility and local inspection, but new deployment automation should use the more explicit probes:

- `--health-check`: liveness only. It is fast, does not perform network calls, and answers whether the process is alive and minimally configured.
- `--readiness-check`: liveness plus applicable guardrails. It answers whether the node is ready to receive traffic. It supports `--json`.
- `--lakona-game-check`: local/developer compatibility surface for the readiness output.

Liveness failure must imply readiness failure. Readiness failure does not necessarily imply liveness failure; for example, a missing hotfix assembly means the process is alive but not ready.

Cluster profile liveness validates node id, advertised endpoints, configured cluster services, and non-empty endpoint keys/values. Standalone liveness validates node id and configured business endpoints, and skips cluster service graph requirements.

Readiness output should format diagnostics for humans:

```txt
cluster: ok single-node
node: ok dev-1
services: ok node-directory, route-directory, gateway
hotfix: failed local build output not found
fix: dotnet build Server/Hotfix/Server.Hotfix.csproj
reliable-push: ok pending limit 256, replay window 120s
rpc: ok kcp://127.0.0.1:20000
```

The generated check command may add friendly grouping and project-specific wording, but it must not maintain a separate validation logic fork. Framework validators own the rules; generated code owns presentation.

When Hotfix build output is missing, the check command should print the repair command and fail. It should not report a degraded or warning-only state.

The check command should also support machine-readable output:

```bash
dotnet run --project Server/Server/Server.csproj -- --lakona-game-check --json
```

Suggested JSON shape:

```json
{
  "succeeded": false,
  "diagnostics": [
    {
      "code": "ULINK071",
      "severity": "error",
      "message": "Hotfix assembly was not found.",
      "repair": "dotnet build Server/Hotfix/Server.Hotfix.csproj"
    }
  ]
}
```

The text output is for humans. The JSON output is for CI, deployment checks, IDE integration, and future `lakona doctor` commands.

## Configuration Boundary

Default generated configuration should remain compact:

```json
{
  "Lakona.Game": {
    "Node": {
      "Id": "dev-1"
    },
    "Endpoints": [
      {
        "Transport": "kcp",
        "Host": "127.0.0.1",
        "Port": 20000
      }
    ]
  }
}
```

Advanced configuration should express source values, not derived internals.

Acceptable advanced values:

- node id
- endpoint transports, hosts, ports, paths, and advertised addresses
- compact `Lakona.Game:Feature` selection for process-local startup composition
- topology or operational profile selected outside the framework schema
- persistent storage provider and connection string names

Advanced configuration should still use user-facing Lakona.Game concepts:

```json
{
  "Lakona.Game": {
    "Node": {
      "Id": "gateway-1"
    },
    "Feature": ["login", "battle"],
    "Endpoints": [
      {
        "Transport": "websocket",
        "Host": "0.0.0.0",
        "Port": 20000,
        "Path": "/ws",
        "AdvertisedHost": "game.example.com"
      },
      {
        "Transport": "kcp",
        "Host": "0.0.0.0",
        "Port": 20001,
        "AdvertisedHost": "game.example.com"
      }
    ]
  }
}
```

This keeps advanced configuration centered on framework-owned source values instead of exposing the internal object graph.

Avoid user-facing defaults for:

- `Hotfix.Enabled`
- `Cluster.Enabled`
- `ReliablePush.Enabled`
- `Hotfix.Directory`
- `ReliablePush.Outbox`
- `Node.Profile`
- `Deployment`
- `Services`
- `Cluster.Directory`
- top-level business endpoint sections such as `ControlPlane` or `Realtime`
- derived bootstrap endpoints
- derived service lists for the default local topology

## Implementation Phases

### Phase 1: Foundation

- Add diagnostic result types.
- Add a resolved runtime model with value provenance.
- Add validators for node id, endpoint transport/path, duplicate service names, and hotfix assembly availability.
- Add unit tests for each diagnostic.

### Phase 2: Check Command Integration

- Make generated `--lakona-game-check` call the framework validation pipeline.
- Keep current readable output shape.
- Add `--json` output for automation and CI.
- Ensure missing Hotfix build output returns a clear repair command.

### Phase 3: Profile-Aware Validation

- Add development and production validation profiles.
- Promote loopback advertised endpoints and in-memory directory storage to production errors.
- Keep local-only development defaults warning-only when they do not violate core runtime invariants.

### Phase 4: Reliable Push And Cluster Readiness

- Validate Reliable Push session identity and resume identity dependencies.
- Validate gateway dependencies on route-directory and node-directory capabilities.
- Validate advertised endpoint reachability rules that can be checked without opening sockets.

## Success Criteria

A generated development project should still run with minimal configuration and no manual edits.

Common local mistakes should fail with specific repair guidance.

Production-oriented configuration should not silently accept local-only defaults.

Tooling and framework startup should use the same validation rules, so a project that passes `--lakona-game-check` has the same basic runtime invariants that server startup expects.
