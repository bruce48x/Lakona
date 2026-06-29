# Lakona Observability Design

## Purpose

Lakona should provide a useful observability baseline for game server operators
without forcing every project to assemble logging, local diagnostics, metrics,
and tracing from scratch.

The first goal is practical troubleshooting. A developer or operator should be
able to answer common questions quickly:

- Is the server running and accepting connections?
- Which transport or endpoint is failing?
- Are actors accumulating mailbox backlog?
- Are sessions, connections, or cluster sends failing?
- Did hotfix reload, activation, or command dispatch fail?
- Where are recent framework warnings and errors?

Lakona should still follow standard .NET diagnostics. Framework packages should
instrument with `ILogger`, `Meter`, and `ActivitySource`; exporter choices such
as Serilog, Prometheus, and OpenTelemetry should be optional integrations.

## Goals

- Add a coherent `Lakona:Observability` configuration surface.
- Make framework stdout logging easy to enable, disable, and filter by category
  and level.
- Keep file logging and log rotation out of core runtime dependencies, but make
  them available through an optional Serilog integration package.
- Generalize the existing hotfix admin listener into a local admin host that can
  serve hotfix, diagnostics, and optional metrics routes through one security
  boundary.
- Provide default local diagnostics for process, RPC, transport, actor,
  session, cluster, and hotfix state.
- Provide a bounded recent diagnostics event buffer for framework warnings,
  errors, slow operations, dead letters, timeouts, and delivery failures.
- Keep metrics and tracing instrumentation always available through standard
  .NET APIs while leaving exporters disabled by default.
- Preserve a safe production posture: no accidental non-loopback diagnostics
  exposure, no default detail dumps, and no secrets or payloads in diagnostics.

## Non-Goals

- Do not build a full observability platform in core Lakona.
- Do not make core runtime packages depend on Serilog, OpenTelemetry exporters,
  Prometheus libraries, or a specific logging backend.
- Do not expose RPC payloads, tokens, user private fields, actor ids, or session
  ids from default summary diagnostics.
- Do not implement a local trace viewer or in-memory span store in the first
  phase.
- Do not encourage high-cardinality metric tags.
- Do not require production users to expose any local admin or metrics endpoint.

## Recommended Approach

Use a lightweight built-in core with optional integrations:

```txt
Runtime packages
  -> Microsoft.Extensions.Logging
  -> System.Diagnostics.Metrics
  -> System.Diagnostics.Activity

Lakona.Game.Server
  -> Runtime packages
  -> LakonaLocalAdminHost
  -> diagnostics snapshot and event model

Optional observability packages
  -> Lakona.Game.Server
  -> Serilog / OpenTelemetry / Prometheus libraries
```

This approach gives a strong default troubleshooting experience while keeping
package boundaries clean. Runtime packages emit logs, metrics, and spans
through standard .NET APIs. Diagnostic events must use primitives owned by the
package that owns the signal, or be derived by `Lakona.Game.Server` from
standard diagnostics during host composition. Lower packages must not depend on
`Lakona.Game.Server` only to publish local diagnostics. `Lakona.Game.Server`
owns the local admin host and aggregates diagnostics. Optional packages wire
those signals to file logging, Prometheus, and OpenTelemetry backends.

## Package Boundaries

Core instrumentation belongs in the packages that own the behavior:

- `Lakona.Rpc.*`: RPC inbound/outbound calls, dispatch failures, protocol
  errors, serialization failures, connection/session transitions where owned.
- `Lakona.Rpc.Transport.*`: listener state, active connections, bytes in/out,
  messages in/out, close reasons, send backpressure, transport errors.
- `Lakona.Game.Server`: process-local game server lifecycle, sessions,
  reliable push, actor kernel diagnostics, local admin host, diagnostic
  snapshots, and event buffer.
- `Lakona.Game.Cluster*`: route lookup, local dispatch, remote dispatch, stale
  registration, node communication, delivery failures.
- `Lakona.Game.Server.Hotfix*`: hotfix load, validation, reload, activation,
  rollback, command dispatch, and reload failures.

`Lakona.Game.Server` owns:

- `Lakona:Observability` options.
- `LakonaLocalAdminHost`.
- diagnostics route registration and JSON serialization.
- process/runtime diagnostics providers.
- the host-level diagnostics event sink and bounded ring buffer.
- startup validation and production guardrails for local observability.

Diagnostic event primitives must not create upward dependencies. The owning
package may define a package-local diagnostic event or callback type, and
`Lakona.Game.Server` may bridge that signal into its host-level event buffer.
For example, RPC packages can expose RPC diagnostics through RPC-owned types or
standard `ILogger`/`ActivitySource` events; actor diagnostics can expose
actor-owned sanitized DTOs; the host-level `IDiagnosticsEventSink` remains a
server composition detail.

Optional integration packages should be separate:

- `Lakona.Game.Server.Observability.Serilog` for file logging, rolling, size
  limits, retention, and structured sinks.
- `Lakona.Game.Server.Observability.OpenTelemetry` for trace and metric export.
- `Lakona.Game.Server.Observability.Prometheus` if Prometheus endpoint support
  grows beyond a small built-in text endpoint.

## Configuration Model

All framework-owned observability configuration lives under
`Lakona:Observability`.

```json
{
  "Lakona": {
    "Observability": {
      "Logging": {
        "Enabled": true,
        "MinimumLevel": "Information",
        "Categories": {
          "Lakona.Rpc": "Information",
          "Lakona.Game.Actor": "Information",
          "Lakona.Game.Session": "Information",
          "Lakona.Game.Cluster": "Information",
          "Lakona.Game.Hotfix": "Information"
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
        "Enabled": null,
        "Host": "127.0.0.1",
        "Port": null,
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

`LocalAdmin.Enabled = null` means defaults from the resolved Lakona runtime
profile, not from the raw `DOTNET_ENVIRONMENT` string:

- `Development`: local admin enabled.
- `Compose`: local admin disabled unless explicitly enabled.
- `Production`: local admin disabled unless explicitly enabled.

Node-specific environment names such as `battle-1` select configuration files
and must not be interpreted as development. Startup and readiness validation
must use the resolved `LakonaGameRuntimeProfile` from the runtime model.

`LocalAdmin.Port = null` means the unified local admin host uses the existing
hotfix admin default port or the framework default selected during
implementation. The observability design should not silently move existing
hotfix admin users to an unrelated port.

`Metrics.Prometheus.Enabled` controls the Prometheus endpoint, not `Meter`
instrumentation. `Tracing.Export.Enabled` controls trace export, not
`ActivitySource` instrumentation. Instrumentation remains present and cheap when
there is no listener.

If `Logging.File.Enabled = true` and the Serilog integration package is not
installed, startup validation should fail with a clear configuration error.
Silent fallback to console would mislead users during incident response.

The same rule applies to exporter features. If `Tracing.Export.Enabled = true`
and no OpenTelemetry integration is registered, startup validation should fail.
If `Metrics.Prometheus.Enabled = true` and no Prometheus endpoint implementation
is registered, startup validation should fail. A future minimal built-in
Prometheus endpoint can satisfy that requirement, but it must be explicit in the
service registration and tests.

## Logging

Lakona should use `Microsoft.Extensions.Logging` categories consistently. The
default categories should be stable and documented:

- `Lakona.Rpc`
- `Lakona.Rpc.Transport`
- `Lakona.Game.Server`
- `Lakona.Game.Session`
- `Lakona.Game.Actor`
- `Lakona.Game.Cluster`
- `Lakona.Game.Hotfix`
- `Lakona.Game.Observability`

Framework logging should focus on operationally useful events:

- server start, stop, and fatal lifecycle failures;
- listener bind and close;
- accepted and rejected connections;
- session resume, disconnect, cleanup, and admission failures;
- RPC dispatch errors and serialization failures;
- actor dead letters, slow messages, call timeouts, and mailbox pressure;
- cluster route lookup failures, remote send failures, stale registrations, and
  node communication errors;
- hotfix validation, activation, rollback, reload, command dispatch, and file
  watch failures;
- local admin startup, disabled routes, guardrail failures, and provider
  failures.

Core should not implement file rolling itself. The Serilog integration package
should translate `Lakona:Observability:Logging:File` into a Serilog sink with
rolling interval, retention, and size limit support.

## Local Admin Host

The current hotfix admin listener should become `LakonaLocalAdminHost`.

The local admin host owns one listener, one binding policy, and one routing
table:

```txt
GET  /_lakona/hotfix/status
POST /_lakona/hotfix/activate
POST /_lakona/hotfix/rollback
POST /_lakona/hotfix/reload
GET  /_lakona/diagnostics/summary
GET  /_lakona/diagnostics/netstat
GET  /_lakona/diagnostics/actors
GET  /_lakona/diagnostics/sessions
GET  /_lakona/diagnostics/events
GET  /_lakona/metrics
```

Hotfix becomes a route module under the local admin host instead of owning a
separate listener. Diagnostics and metrics share the same local security
boundary.

The host should default to `127.0.0.1` and `RequireLoopback = true`.
Non-loopback production binding is a dangerous configuration and should fail
startup unless a future explicit secure remote admin model is added.

Local admin listener availability must be decoupled from production hotfix
package mode. Production hotfix mode selects version-pointer package loading
under `hotfix/versions` and `current.txt`; `LocalAdmin.Enabled` controls only
whether the HTTP admin listener is running. Existing coupling between hotfix
admin enablement and production hotfix assembly source selection should be
removed during the migration.

When local admin is disabled in production, the server can still load the
version pointed to by `current.txt` at startup, but online
`activate`/`status`/`rollback`/`reload` commands are unavailable. Operators who
want the documented v1 online hotfix workflow must explicitly enable local
admin on loopback.

## Diagnostics Endpoints

Diagnostics endpoints should be organized by troubleshooting task rather than
internal implementation class. JSON responses must be bounded and safe by
default.

`GET /_lakona/diagnostics/summary`

Returns node-level overview:

- environment and process id;
- uptime;
- local admin state;
- active listeners;
- active connections;
- active sessions;
- actor type count and total actor count;
- recent warning/error counters;
- hotfix active build tag and reload status;
- cluster local node id and health summary.

`GET /_lakona/diagnostics/netstat`

Returns transport and listener aggregates similar to skynet-style netstat:

- transport name;
- listener endpoint;
- active connections;
- accepted connections;
- closed connections;
- bytes in/out;
- messages in/out;
- send queue or backpressure summary;
- transport error counters.

`GET /_lakona/diagnostics/actors`

Returns actor aggregates:

- actor type;
- active count;
- mailbox queue sum and max;
- accepted, rejected, processed, deadletter, timeout counters;
- slow message counters.

This endpoint must not expose actor ids by default.

`GET /_lakona/diagnostics/sessions`

Returns session aggregates:

- active sessions;
- active connections;
- resumable sessions;
- authenticated versus anonymous counts when available as aggregate state;
- endpoint and transport grouping;
- disconnect and resume counters.

This endpoint must not expose tokens, payloads, or user-specific identifiers.

`GET /_lakona/diagnostics/events`

Returns the recent diagnostics event ring buffer:

- timestamp;
- level;
- category;
- event kind;
- trace id or correlation id when available;
- bounded message summary;
- low-cardinality dimensions such as transport, actor type, feature name, or
  cluster node role where safe.

The event buffer should include dead letters, slow messages, call timeouts,
transport errors, cluster delivery failures, local admin provider failures, and
hotfix failures.

Raw diagnostics must be sanitized before entering the event buffer. Actor
diagnostics currently include actor ids and raw `object` message/request values;
those raw values are acceptable inside internal callback contracts only if the
bridge to local diagnostics replaces them with safe fields such as event kind,
actor type, message type, elapsed time, timeout reason, and bounded error text.
The event buffer must not store actor ids, session ids, connection ids, tokens,
call chains, RPC payloads, request values, or user-specific identifiers unless a
detail endpoint has explicitly opted into that specific field.

Detail endpoints are disabled by default:

```txt
GET /_lakona/diagnostics/actors/{actorId}
GET /_lakona/diagnostics/sessions/{sessionId}
GET /_lakona/diagnostics/connections/{connectionId}
```

They are available only when `Diagnostics.DetailEnabled = true`. Production
detail mode should be treated as risky and must go through guardrails.

## Diagnostics Provider Model

Use provider interfaces so each subsystem owns its own diagnostic data:

```txt
ILakonaDiagnosticsSnapshotProvider
IProcessDiagnosticsProvider
ITransportDiagnosticsProvider
IRpcDiagnosticsProvider
IActorDiagnosticsProvider
IClusterDiagnosticsProvider
IHotfixDiagnosticsProvider
IDiagnosticsEventSink
```

`LakonaLocalAdminHost` should not inspect actor mailboxes, session stores,
transport internals, or hotfix state directly. It asks providers for bounded
snapshots and serializes the result.

Provider failure should not bring down the server. A diagnostics request should
return partial data with a provider error entry, and the event buffer should
record the failure.

## Metrics

Lakona should use `System.Diagnostics.Metrics`.

Recommended meter names:

- `Lakona.Rpc`
- `Lakona.Rpc.Transport`
- `Lakona.Game.Server`
- `Lakona.Game.Session`
- `Lakona.Game.Actor`
- `Lakona.Game.Cluster`
- `Lakona.Game.Hotfix`

Metrics should cover:

- connections accepted, active, closed, rejected;
- bytes and messages in/out;
- RPC calls started, completed, failed, timed out;
- sessions active, resumed, disconnected, expired;
- actor messages accepted, rejected, processed, deadlettered;
- actor call timeouts;
- mailbox queue length as an observable gauge;
- cluster route lookup success/failure and remote send success/failure;
- hotfix reload, activation, rollback, and command dispatch success/failure;
- diagnostics provider failures.

Metric tags must stay low-cardinality. They may include category-like values
such as transport type, configured transport value, actor type, feature name,
command id, cluster direction, and status. They must not include endpoint
display names, endpoint names, actor ids, actor names, session ids, player ids,
tokens, payload values, request values, or user-specific identifiers.

`/_lakona/metrics` is disabled by default. It can expose Prometheus text format
when explicitly enabled and a Prometheus endpoint implementation is registered,
but Prometheus support should not become the only metrics path. OpenTelemetry
metrics export should remain an optional integration.

## Tracing

Lakona should use `System.Diagnostics.ActivitySource`.

Recommended source names:

- `Lakona.Rpc`
- `Lakona.Game.Actor`
- `Lakona.Game.Session`
- `Lakona.Game.Cluster`
- `Lakona.Game.Hotfix`

Priority spans:

- RPC inbound call;
- RPC outbound call;
- actor `Tell`;
- actor `Call`;
- actor timer tick;
- feature command dispatch;
- cluster route lookup;
- cluster remote send;
- hotfix reload;
- hotfix activation;
- hotfix command dispatch.

Trace exporters are disabled by default. The first phase should not implement a
local trace buffer. Recent local troubleshooting should use the diagnostics
event buffer, while distributed tracing should use an optional OpenTelemetry
integration package.

Trace attributes must follow the same sensitive-data policy as metrics and
diagnostics. Include stable operational dimensions; exclude payloads, tokens,
and user-private data.

Existing actor spans that include actor ids or call chains must be changed as
part of the observability work. Actor spans may include actor type, message
type, message kind, timeout reason, and status; they must not include actor ids,
actor names, call chains, request values, or message payloads.

## Guardrails

Defaults are resolved from `LakonaGameRuntimeProfile`, not from raw
environment names. A node-specific environment file such as
`appsettings.battle-1.json` must not accidentally select development defaults.

Development defaults:

- local admin enabled;
- loopback binding required;
- summary diagnostics enabled;
- detail diagnostics disabled;
- event buffer enabled;
- Prometheus endpoint disabled;
- tracing exporter disabled.

Compose and Production defaults:

- local admin disabled;
- summary diagnostics available only if local admin is explicitly enabled;
- detail diagnostics disabled;
- Prometheus endpoint disabled;
- tracing exporter disabled.

Production validation:

- `LocalAdmin.Host` must be loopback when local admin is enabled and
  `RequireLoopback = true`.
- non-loopback local admin binding should fail startup.
- `Diagnostics.DetailEnabled = true` should produce a strong warning even on
  loopback.
- `Diagnostics.DetailEnabled = true` with non-loopback binding should fail
  startup.
- `Logging.File.Enabled = true` without the Serilog integration package should
  fail startup.
- `Tracing.Export.Enabled = true` without the OpenTelemetry integration package
  should fail startup.
- `Metrics.Prometheus.Enabled = true` without a registered Prometheus endpoint
  implementation should fail startup.
- invalid event buffer capacity, invalid metrics path, and invalid log level
  values should fail configuration validation.
- invalid tracing sample rates outside `0.0` through `1.0` should fail
  configuration validation.

Observability guardrails must participate in the same runtime validation
pipeline used by startup and `--readiness-check`. They need stable diagnostic
codes so logs, documentation, JSON check output, and tests can refer to the same
condition. The durable guardrails document should reserve exact codes during
implementation; the design expects codes for non-loopback local admin, unsafe
detail diagnostics, missing file logging integration, missing OpenTelemetry
integration, missing Prometheus implementation, invalid metrics path, invalid
event buffer capacity, invalid log level, and invalid trace sample rate.

## Failure Model

Configuration failures should be clear and early:

- dangerous production exposure: fail startup;
- missing file logging integration when file logging is explicitly enabled:
  fail startup;
- missing OpenTelemetry integration when tracing export is explicitly enabled:
  fail startup;
- missing Prometheus endpoint implementation when the Prometheus endpoint is
  explicitly enabled: fail startup;
- malformed observability options: fail startup.

Runtime diagnostics failures should be isolated:

- a provider exception should not crash the game server;
- the endpoint should return partial data with an error record;
- the event buffer should record the provider failure;
- local admin route failures should be logged under `Lakona.Game.Observability`.

Observability should never block the actor kernel, RPC dispatch, transport I/O,
or hotfix command dispatch on slow exporter work. Exporters must run through the
standard logging, metrics, or tracing pipelines rather than synchronous
framework-owned network calls in the hot path.

## Testing Requirements

Add focused tests for:

- `Lakona:Observability` binding defaults.
- Development, Compose, and Production local admin defaults.
- node-specific environments such as `DOTNET_ENVIRONMENT=battle-1` do not select
  development local admin defaults.
- local admin loopback validation.
- production failure on non-loopback local admin binding.
- warning or validation behavior when `Diagnostics.DetailEnabled = true`.
- startup failure when file logging is enabled without the Serilog integration.
- startup failure when tracing export is enabled without the OpenTelemetry
  integration.
- startup failure when Prometheus is enabled without a registered Prometheus
  endpoint implementation.
- local admin route registration preserves existing hotfix admin behavior.
- local admin disabled does not force development hotfix assembly source
  selection in production hotfix package mode.
- hotfix online activation/status routes remain available when production users
  explicitly enable local admin on loopback.
- diagnostics summary returns bounded JSON.
- netstat returns transport aggregates without connection-private data.
- actor diagnostics returns aggregates without actor ids by default.
- session diagnostics returns aggregates without tokens or user identifiers.
- event buffer capacity and `MinimumLevel` behavior.
- event buffer sanitization excludes actor ids, session ids, connection ids,
  tokens, call chains, payloads, request values, and user-specific identifiers.
- diagnostics provider failures produce partial results and events.
- metrics names and low-cardinality tag policy for critical instruments.
- activity source names and key span creation for RPC, actor, cluster, and
  hotfix paths.
- actor spans exclude actor ids, actor names, call chains, request values, and
  message payloads.
- `--readiness-check` human output and `--readiness-check --json` include the
  same stable observability diagnostic codes as startup validation for unsafe
  local admin binding, unsafe detail diagnostics, bad metrics path, invalid
  sample rate, and missing integrations.

Tests should protect runtime contracts and safety boundaries, not exact
incidental JSON formatting.

## Implementation Phases

Phase 1: configuration, logging, and local admin foundation.

- Add `Lakona:Observability` options.
- Wire framework logging category defaults.
- Generalize hotfix admin into `LakonaLocalAdminHost`.
- Preserve existing hotfix admin routes.
- Add local admin guardrails.
- Decouple local admin listener enablement from production hotfix package mode.

Phase 2: diagnostics snapshots and event buffer.

- Add provider interfaces.
- Add process, transport/RPC, actor, session, cluster, and hotfix snapshot
  providers.
- Add bounded diagnostics event sink.
- Bridge lower-package diagnostics into the host event buffer without adding
  upward package dependencies.
- Sanitize actor, session, connection, transport, RPC, cluster, and hotfix
  events before they enter the event buffer.
- Add summary, netstat, actors, sessions, and events endpoints.
- Ensure responses are bounded and safe by default.

Phase 3: metrics and tracing polish.

- Normalize meter and activity source names.
- Add missing counters, gauges, and spans for critical paths.
- Add tests for low-cardinality tags and span names.
- Keep exporters disabled by default.

Phase 4: optional integrations.

- Add Serilog integration for file logging, rolling, retention, and size limit.
- Add OpenTelemetry integration for traces and metrics.
- Add or refine Prometheus endpoint support only after the metric naming and tag
  policy is stable.

## Documentation Updates

Durable documentation should be updated after implementation:

- `docs/configuration.md` for `Lakona:Observability` schema and environment
  variable shape.
- `docs/guardrails.md` for local admin and production exposure rules.
- `docs/cluster.md` for cluster diagnostics and delivery failure visibility.
- `docs/actor.md` for actor metrics, events, and diagnostics aggregates.
- `docs/hotfix/architecture.md` for local admin route migration and hotfix
  observability.
- package READMEs for optional Serilog and OpenTelemetry integrations.

Completed planning and review notes under `docs/superpowers/**` should be
deleted or moved into durable docs when the work is complete, following the
repository maintenance rules.
