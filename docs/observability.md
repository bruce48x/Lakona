# Observability

Lakona emits telemetry through the standard .NET diagnostics APIs and leaves
collection, export, storage, dashboards, and alerting to the application.
There is no Lakona-specific telemetry protocol or exporter.

## Ownership Boundary

Lakona owns signal production:

- `System.Diagnostics.Metrics.Meter` for metrics
- `System.Diagnostics.ActivitySource` for traces
- `Microsoft.Extensions.Logging.ILogger` for logs
- W3C trace context for propagation

The application owns the OpenTelemetry SDK, sampling, processors, exporters,
Collector topology, retention, dashboards, and alert rules. This lets the same
Lakona process work with OTLP, Prometheus, Grafana, Jaeger, Tempo, Loki,
Application Insights, or another OpenTelemetry-compatible stack without a
Lakona adapter.

Health probes are intentionally separate from telemetry. When enabled,
`GET /_lakona/health/live` and `GET /_lakona/health/ready` remain lightweight
HTTP endpoints for an orchestrator or load balancer.

## Instrumentation Scopes

Use `Lakona.Game.Server.Observability.LakonaGameServerTelemetry` as the stable
catalog of meter and activity-source names. Current scopes are:

| Signal | Scope |
| --- | --- |
| Metrics | `Lakona.Game.Actor` |
| Metrics and traces | `Lakona.Game.Cluster` |
| Metrics | `Lakona.Game.ReliablePush` |
| Metrics | `Lakona.Rpc.Server` |
| Metrics | `Lakona.Game.Session` |
| Metrics | `Lakona.Game.Timer` |
| Traces | `Lakona.Game.Actor` |

Metric names use the `lakona.game.*` namespace. Population gauges intentionally
avoid actor ids, session ids, timer ids, and other high-cardinality tags.

The Actor meter emits `lakona.game.actor.activation.active`,
`lakona.game.actor.activation.metadata`, and
`lakona.game.actor.activation.released`. Actor Location removes released
records instead of retaining tombstones, so the released gauge is currently
zero by design; the metadata gauge reports the active recovery records kept by
the local Actor activation registry.

The cluster scope currently emits these control-plane signals:

| Signal | Kind | Tags or activity name |
| --- | --- | --- |
| `lakona.game.cluster.membership.request` | Counter | `lakona.game.cluster.outcome`: `success`, `timeout`, `canceled`, or `failure` |
| `lakona.game.cluster.membership.request.duration` | Histogram, seconds | same bounded outcome tag |
| `lakona.game.cluster.authority.transition` | Counter | `lakona.game.cluster.authority.state`: `available`, `lost`, or `transient_failure` |
| `lakona.game.cluster.actor_location.recovery.duration` | Histogram, seconds | bounded recovery outcome tag |
| `lakona.game.cluster.actor_location.failure` | Counter | `lakona.game.cluster.reason`: `unavailable`, `conflict`, or `capacity` |
| `lakona.game.cluster.actor_request.proof_failure` | Counter | `lakona.game.cluster.reason`: `cluster_incarnation`, `local_node`, `target_node`, `node_incarnation`, `membership_view`, `directory_unavailable`, or `activation` |
| `cluster.membership.request` | Activity | one outbound Membership RPC |
| `cluster.actor_location.stabilize` | Activity | one Actor Location recovery/stabilization run |

These instruments intentionally omit node, actor, route, and exception text
from metric labels. Put those details in sampled activities or structured
logs. Alert readiness separately: `/_lakona/health/ready` becomes unhealthy
when distributed admission is closed because current authority is absent.

The `Lakona.Game.Session` meter emits
`lakona.game.notification.backpressure` whenever notification admission is
rejected. Its bounded `lakona.game.notification.reason` tag distinguishes
`session_capacity`, `process_capacity`, and `batch_bytes`; it never carries a
session, owner, callback, or gateway identifier.

The `Lakona.Rpc.Server` meter emits one `request.started` counter for every
request accepted by a Session and exactly one `request.outcome` counter plus
`request.duration` sample when that request reaches a terminal state. The
bounded `lakona.rpc.request.outcome` values are `response`, `canceled`,
`connection_closed`, and `failure`; `lakona.rpc.response.status_code` is
present when a response status is known. Requests that enter the Session
concurrency budget also emit `request.queue.duration`. All RPC request metrics
carry only numeric service and method ids plus these bounded outcome/status
attributes; request and connection ids remain in structured logs.

## OpenTelemetry Setup

Install the OpenTelemetry packages required by the application and configure
them in the application composition root. A typical OTLP setup is:

```csharp
using Lakona.Game.Server.Observability;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

server.AddServices(services =>
{
    services.AddOpenTelemetry()
        .ConfigureResource(resource => resource.AddService("game-gateway"))
        .WithMetrics(metrics => metrics
            .AddMeter(LakonaGameServerTelemetry.MeterNames.ToArray())
            .AddRuntimeInstrumentation()
            .AddProcessInstrumentation()
            .AddOtlpExporter())
        .WithTracing(tracing => tracing
            .AddSource(LakonaGameServerTelemetry.ActivitySourceNames.ToArray())
            .AddAspNetCoreInstrumentation()
            .AddOtlpExporter());
});

server.ConfigureLogging(logging => logging.AddOpenTelemetry(options =>
{
    options.IncludeFormattedMessage = true;
    options.IncludeScopes = true;
    options.AddOtlpExporter();
}));
```

The exporter can use standard `OTEL_*` environment variables. For example:

```bash
OTEL_SERVICE_NAME=game-gateway
OTEL_EXPORTER_OTLP_ENDPOINT=http://otel-collector:4317
OTEL_EXPORTER_OTLP_PROTOCOL=grpc
```

Replace `AddOtlpExporter` with the exporter required by the deployment. Lakona
does not need to know whether the Collector writes to Prometheus, Tempo,
Jaeger, Loki, or a hosted observability service.

## Multi-node Deployment

Run one OpenTelemetry SDK pipeline per Lakona process and attach stable resource
attributes such as service name, deployment environment, region, and instance
id. Send every node to the same Collector tier. Host CPU, memory, GC, process,
and network signals should come from standard runtime/process/host
instrumentation rather than custom Lakona endpoints.

Recommended first dashboards combine:

- host CPU, memory, network throughput, and process restarts
- `lakona.game.actor.activation.active` and mailbox queue length
- `lakona.game.session.active`, active connections, and resumable sessions
- Actor Location unavailable/recovery outcomes, Actor request proof failures,
  and notification backpressure
- RPC request rate, terminal outcome, response status, queue delay, and
  end-to-end latency
- timer capacity rejections and reliable-push continuity loss
- application HTTP request rate, error rate, and latency from ASP.NET Core instrumentation

Keep ids and unbounded business values out of metric labels. Put detailed
request context in sampled traces or structured logs instead.

## Management Configuration

Hotfix admin routes, when needed, are enabled independently from telemetry:

```json
{
  "Lakona": {
    "Management": {
      "Http": {
        "Host": "127.0.0.1",
        "Port": 20080
      },
      "Admin": {
        "Enabled": true,
        "RequireLoopback": true
      }
    },
    "Health": {
      "Enabled": true,
      "RequireLoopback": true
    }
  }
}
```

The removed `Lakona:Observability` section and
`/_lakona/diagnostics/*` routes are not compatibility aliases. Startup rejects
the old configuration so a deployment cannot silently believe telemetry is
being exported when it is not.
