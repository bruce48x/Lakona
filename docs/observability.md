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
- Actor Location unavailable/recovery outcomes and notification backpressure
- RPC request rate, response status, and dispatch latency
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
