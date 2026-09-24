# Guardrails

Lakona guardrails validate runtime configuration and generated project shape
before a server starts. They enforce the current runtime and configuration
contracts.

## Ownership And Validation API

Guardrails check framework configuration. Application configuration and resource
initialization belong in `ILakonaModule` in `Server.App`; a failed module startup
already prevents readiness and triggers cleanup. Guardrails do not expose a
custom rule registration mechanism or track which configuration provider supplied
a value.

`LakonaGameRuntimeValidator` reads `LakonaGameRuntimeOptions` directly. Its
optional `ClusterOptions` supplies the effective node identity, and its optional
Hotfix assembly path selects the actual startup artifact. Startup and readiness
use the same built-in checks. Indexed error paths are constructed at the check
site; no second configuration model is maintained.

### Upgrade To Game.Server 0.50.0

This release removes `ILakonaGameValidationRule`, `LakonaGameResolved*`, and
`LakonaGameValueSource`; the rule classes in `Guardrails.Rules` become internal.
Consumers of those APIs must rebuild and migrate:

- Move custom rule logic and its DI registrations to the appropriate application
  module's startup checks. Throw a descriptive exception when startup must fail;
  the normal module readiness diagnostic reports the failure.
- Replace construction of resolved DTOs and injected rule lists with
  `new LakonaGameRuntimeValidator().Validate(runtimeOptions, clusterOptions,
  hotfixAssemblyPath)`. The last two arguments are optional; normal server
  startup supplies them automatically.
- Stop using resolved values for provider-source inspection; the framework does
  not offer that capability. Read application options for effective values.

`AddLakonaGameRuntimeValidation` still registers the built-in validator. Existing
framework diagnostic IDs, severities, repair hints, indexed configuration paths,
and the readiness HTTP response shape remain unchanged. Applications using only
the standard server entry point need no source changes.

## Readiness Scope

Readiness validation checks:

- node id and advertised endpoint shape
- endpoint transport, serializer, host, port, WebSocket path, active-connection
  capacity, pending-handshake capacity, and handshake deadline
- duplicate endpoint transports and duplicate RPC service names
- cluster endpoint URI
- blank and duplicate node roles
- heartbeat interval and timeout
- hotfix assembly source
- management admin listener exposure
- application module and full framework startup state

Stable application dependencies implement `ILakonaModule` in `Server.App`.
Lakona discovers and initializes them before initial Hotfix loading, management
HTTP, RPC listeners, cluster Ready publication, and Startup Actors. Module
startup failure fails the process and reverses already-started modules.
Pending, failed, and stopping lifecycle states appear in the normal readiness
snapshot. See [Application Modules](./application-modules.md).

Run readiness validation through the health route on the management HTTP listener:

```bash
curl http://127.0.0.1:20080/_lakona/health/ready
```

The endpoint returns JSON and uses HTTP 200 when ready or HTTP 503 when any
guardrail diagnostic is fatal.

## Diagnostic IDs

All Lakona diagnostic codes use `LAKONA` followed by exactly five decimal
digits. The first digit identifies the owning area; the remaining four digits
identify the rule. IDs are globally unique across areas, remain stable once
assigned, and must not be reused for a different rule. Gaps are intentional;
reserved ranges do not imply that every ID is implemented.

| Range | Owner |
| --- | --- |
| `LAKONA10000-LAKONA19999` | Runtime configuration and readiness |
| `LAKONA20000-LAKONA29999` | Hotfix analyzers and generators |
| `LAKONA30000-LAKONA39999` | RPC contract analyzer |
| `LAKONA40000-LAKONA49999` | RPC source generator |
| `LAKONA50000-LAKONA59999` | Project generation plan validation |

Other leading digits are reserved. Diagnostic severity is independent of its
ID. Allocate new rules within their owner's range and check for collisions.

### Migration from previous IDs

This release changes diagnostic identifiers, not rule behavior or severity.
Update `.editorconfig` diagnostic keys, `NoWarn`, `WarningsAsErrors`,
`WarningsNotAsErrors`, `#pragma warning`, `SuppressMessage`, rulesets, tests,
and monitoring filters that refer to the previous identifiers. Old IDs are
not emitted as aliases. Existing runtime health consumers must accept the new
codes when upgrading the server.

| Previous ID | New ID |
| --- | --- |
| `LAKONA001` through `LAKONA199` | Add 10000 to the numeric suffix |
| `LKNHOTFIXnnn` | Add 20000 to the numeric suffix: `LKNHOTFIX032` becomes `LAKONA20032` |
| `ULRPC001` through `ULRPC006` | `LAKONA30001` through `LAKONA30006` |
| `ULRPCGEN001` | `LAKONA40001` |
| `LTPLAN001` through `LTPLAN007` | `LAKONA50001` through `LAKONA50007` |

The Hotfix compiler extension ships with `Lakona.Game.Server`; the RPC
compiler extension ships with `Lakona.Rpc.Core`. Upgrade these packages and
rebuild consuming projects. Project plan codes ship through Lakona.Tool and
Lakona Hub. Keep configuration changes aligned with the package upgrade.

### Runtime Diagnostics Ranges

- `LAKONA10001-LAKONA10019`: node identity and common runtime shape
- `LAKONA10020-LAKONA10039`: endpoint transport and RPC service configuration
- `LAKONA10040-LAKONA10069`: cluster endpoint, membership, node discovery, and route directory
- `LAKONA10070-LAKONA10089`: hotfix source and reload readiness
- `LAKONA10090-LAKONA10099`: heartbeat policy
- `LAKONA10101-LAKONA10109`: node role configuration
- `LAKONA10130-LAKONA10149`: management admin exposure
- `LAKONA10150-LAKONA10159`: application module and server lifecycle readiness

## Production Boundary

Production processes should fail before opening listeners when configuration is
ambiguous or unsafe. In particular:

- cluster endpoints must use the framework-owned TCP scheme; cluster formation
  and discovery use the shared [Membership Table](./cluster.md#membership-table)
- cluster peers must complete cluster protocol negotiation before RPC starts
- WebSocket endpoints require a path
- KCP and TCP endpoints must not use HTTP paths
- endpoint connection limits must be positive, pending handshakes cannot exceed
  active connections, and the handshake deadline must be positive
- node role entries must be non-empty and unique; Actor hosting follows
  [typed declarations and node roles](./configuration.md#node-roles-and-actor-hosting)
- management admin routes must remain loopback-only unless explicitly deployed on a trusted network
- every application module must complete startup before the node publishes
  Ready or opens application listeners

## Generated Projects

Generated starter projects should keep `appsettings.json` compact. Derived
runtime state is shown by the readiness endpoint rather than copied into
generated configuration. When a generated project is split across nodes, use
`Lakona:Node:Roles`, Actor and Module `[NodeRole]` declarations, `Lakona:Endpoints[]`, endpoint `RpcServices`, and
`Lakona:Cluster`; declare Startup Actor groups in
`HotfixStartup.ConfigureActors`. Exact key shapes and defaults belong to
[Configuration](./configuration.md).
