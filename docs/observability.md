# Observability

Lakona emits telemetry through the standard .NET diagnostics APIs and leaves
collection, export, storage, dashboards, and alerting to the application.
There is no Lakona-specific telemetry protocol or exporter.

To monitor a cluster, follow [OpenTelemetry Setup](#opentelemetry-setup),
[Multi-node Deployment](#multi-node-deployment), and
[Collector and Dashboards](#collector-and-dashboards) in order, then run the
[acceptance checks](#verify-and-troubleshoot). The same procedure applies to
generated projects and custom hosts.

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

Game metric names use `lakona.game.*`; RPC request metrics use
`lakona.rpc.server.*`. Population gauges intentionally
avoid actor ids, session ids, timer ids, and other high-cardinality tags.

The Actor meter emits `lakona.game.actor.activation.active`,
`lakona.game.actor.activation.metadata`, and
`lakona.game.actor.activation.released`. Actor Directory removes released
records instead of retaining tombstones, so the released gauge is currently
zero by design; the metadata gauge reports recoverable claims held by the local
`ActorActivationCatalog`.

The cluster scope currently emits these control-plane signals:

| Signal | Kind | Tags or activity name |
| --- | --- | --- |
| `lakona.game.cluster.membership.table.operation` | Counter | bounded `lakona.game.cluster.operation` and `lakona.game.cluster.outcome` tags |
| `lakona.game.cluster.membership.table.operation.duration` | Histogram, seconds | same bounded operation and outcome tags |
| `lakona.game.cluster.membership.lifecycle` | Counter | `lakona.game.cluster.membership.state`: `joining`, `active`, `stopping`, `dead`, `fenced`, or `table_unavailable` |
| `lakona.game.cluster.actor_directory.transition.duration` | Histogram, seconds | `lakona.game.cluster.outcome`: `success`, `failure`, or `cancelled`; `lakona.game.cluster.actor_directory.mode`: `handoff` or `recovery` |
| `lakona.game.cluster.actor_directory.failure` | Counter | `lakona.game.cluster.reason`: `unavailable` or `conflict` |
| `lakona.game.cluster.actor_request.proof_failure` | Counter | `lakona.game.cluster.reason`: `cluster_incarnation`, `local_node`, `target_node`, `node_incarnation`, `membership_view`, `directory_unavailable`, or `activation` |
| `cluster.membership.table` | Activity | one Membership Table operation |
| `cluster.actor_directory.transition` | Activity | one Actor Directory range transition |

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

Install these packages in the stable application host, not the reloadable
Hotfix project. From a generated project's root:

```bash
dotnet add Server/App/Server.App.csproj package OpenTelemetry.Extensions.Hosting
dotnet add Server/App/Server.App.csproj package OpenTelemetry.Exporter.OpenTelemetryProtocol
dotnet add Server/App/Server.App.csproj package OpenTelemetry.Instrumentation.Runtime
dotnet add Server/App/Server.App.csproj package OpenTelemetry.Instrumentation.AspNetCore
```

Pin compatible versions in your application's dependency management. Inside
the existing `LakonaGameServer.RunAsync` configuration callback in
`Server/App/Program.cs`, add the following registrations before the host starts.
Here `server` is that callback's `LakonaGameServerBuilder`; preserve existing
transport, serializer, Membership Adapter, and application registrations.

```csharp
using Lakona.Game.Server.Observability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

server.AddServices(services =>
{
    services.AddOpenTelemetry()
        .WithMetrics(metrics => metrics
            .AddMeter(LakonaGameServerTelemetry.MeterNames.ToArray())
            .AddRuntimeInstrumentation()
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

Set these variables in the server process environment (for example, `export`
in Bash or `$env:OTEL_SERVICE_NAME = 'game-gateway'` in PowerShell). The SDK's
default resource reads `OTEL_SERVICE_NAME` and `OTEL_RESOURCE_ATTRIBUTES` for
all three signals; this example deliberately leaves resource identity in
deployment configuration. Environment variables alone do not register an SDK
or subscribe it to Lakona's meters and activity sources.

The [OpenTelemetry .NET exporter guide](https://opentelemetry.io/docs/languages/dotnet/exporters/)
describes exporter installation and protocol options. Runtime instrumentation
covers .NET runtime signals; add separate host/process instrumentation for
machine and network monitoring as needed.

Replace `AddOtlpExporter` with the exporter required by the deployment. Lakona
does not need to know whether the Collector writes to Prometheus, Tempo,
Jaeger, Loki, or a hosted observability service.

## Multi-node Deployment

Run one OpenTelemetry SDK pipeline per Lakona process and attach stable resource
attributes such as service name, deployment environment, region, and instance
id. Send every node to the same Collector tier. Host CPU, memory, GC, process,
and network signals should come from standard runtime/process/host
instrumentation rather than custom Lakona endpoints.

For example, apply the common OTLP endpoint above to all nodes and give each
process its own identity:

| Process | `OTEL_SERVICE_NAME` | `service.instance.id` |
| --- | --- | --- |
| Gateway | `game-gateway` | `gateway-1` |
| Data | `game-data` | `data-1` |
| Battle | `game-battle` | `battle-1` |

On `gateway-1`, set:

```bash
OTEL_RESOURCE_ATTRIBUTES=service.namespace=my-game,deployment.environment.name=production,service.instance.id=gateway-1
```

Use the same namespace and environment for the other nodes, replacing the
instance id. Replicas of a role share a service name but must have distinct
instance ids; use the pod UID or another process-instance identity when
instances can overlap. These are telemetry resource attributes, not Membership
configuration. They do not set `Lakona:Node:Id` or determine cluster admission.
See [OpenTelemetry resources](https://opentelemetry.io/docs/languages/dotnet/resources/).

The monitoring tier is ordinary infrastructure outside Lakona Membership:

```text
Gateway / Data / Battle -- OTLP --> Collector -- metrics --> Prometheus
                                            -- traces  --> Tempo / Jaeger
                                            -- logs    --> OTLP-capable log backend
                                                           |
                                                        Grafana
```

The Collector may run on a monitoring host, as an agent on each host, or behind
a shared ingestion endpoint. A Collector outage is a telemetry delivery issue;
it does not provide evidence that Membership or readiness has failed.

## Collector and Dashboards

### Start collection

Install an OpenTelemetry Collector distribution containing the `otlp`
receiver, `batch` processor, and `prometheus` and `debug` exporters, such as
the contrib distribution. Pin its version in deployment configuration and
validate its configuration before starting it. Save this as
`otel-collector.yaml` on the monitoring host:

```yaml
receivers:
  otlp:
    protocols:
      grpc:
        endpoint: 0.0.0.0:4317
processors:
  batch: {}
exporters:
  prometheus:
    endpoint: 0.0.0.0:9464
  debug:
    verbosity: detailed
service:
  pipelines:
    metrics:
      receivers: [otlp]
      processors: [batch]
      exporters: [prometheus]
    traces:
      receivers: [otlp]
      processors: [batch]
      exporters: [debug]
    logs:
      receivers: [otlp]
      processors: [batch]
      exporters: [debug]
```

Run the installed binary (use `otelcol-contrib.exe` on Windows):

```bash
otelcol-contrib validate --config=otel-collector.yaml
otelcol-contrib --config=otel-collector.yaml
```

Resolve `otel-collector` in the node environment to this host, or replace that
name in `OTEL_EXPORTER_OTLP_ENDPOINT` with its reachable address. For containers,
publish port 4317 to the game-node network and 9464 to the Prometheus network;
`localhost` inside a game container refers to that container.

This bootstrap configuration retains metrics in Prometheus once scraping is
configured below; it only prints traces and logs for verification. For durable
traces and logs, replace `debug` in each pipeline with the chosen backend's
OTLP exporter, endpoint, credentials, and TLS settings. A component declaration
alone does not enable it: include it in the relevant pipeline. Remove detailed
debug output after verification. Use private network access and configured
TLS/authentication when traffic crosses a trust boundary. Size Collector memory,
queues, retries, and storage for the expected telemetry volume.

See the [Collector configuration reference](https://opentelemetry.io/docs/collector/configuration/)
for pipeline and transport configuration.

### Connect Prometheus and Grafana

Add this scrape job to an installed Prometheus server's `prometheus.yml`, then
reload or restart Prometheus using its deployment's normal procedure:

```yaml
scrape_configs:
  - job_name: lakona-collector
    scrape_interval: 15s
    honor_labels: true
    static_configs:
      - targets: ['otel-collector:9464']
```

Replace the target with the Collector address reachable from Prometheus.
Prometheus scrapes the Collector's `/metrics`, not a Lakona management route.
The exporter maps service namespace/name to `job` and service instance id to
`instance`; `honor_labels` preserves these application identities. Additional
resource attributes are available through `target_info`. Avoid copying every
resource attribute onto every metric. Exported metric names can normalize dots
and append unit/type suffixes; inspect `/metrics` before writing queries for
your pinned exporter version. See the
[Prometheus exporter reference](https://github.com/open-telemetry/opentelemetry-collector-contrib/blob/main/exporter/prometheusexporter/README.md).

In Grafana, add a Prometheus data source pointing to the Prometheus server's
HTTP URL (normally port 9090), then use Explore to find `lakona` metrics.
With the exporter's default underscore-and-suffix translation, start with:

```promql
# Accepted RPC requests per second, grouped by service and process.
sum by (job, instance) (rate(lakona_rpc_server_request_started_total[5m]))

# RPC p95 duration in seconds, merging histogram buckets rather than percentiles.
histogram_quantile(0.95,
  sum by (job, le) (rate(lakona_rpc_server_request_duration_seconds_bucket[5m])))
```

Save panels with service/instance filters, then add the relevant trace and log
data sources after configuring their durable exporters. Search traces by
service and a time window containing real Actor or Membership work; correlate
logs by trace id where the log was emitted inside an activity. Do not assume
every log has a trace id or that ASP.NET Core instrumentation covers Lakona RPC.

### Choose panels and alerts

Recommended first dashboards combine:

- host CPU, memory, network throughput, and process restarts
- `lakona.game.actor.activation.active` and mailbox queue length
- `lakona.game.session.active`, active connections, and resumable sessions
- Actor Directory unavailable/transition outcomes, Actor request proof failures,
  and notification backpressure
- RPC request rate, terminal outcome, response status, queue delay, and
  end-to-end latency
- timer capacity rejections and reliable-push continuity loss
- application HTTP request rate, error rate, and latency from ASP.NET Core instrumentation

Keep ids and unbounded business values out of metric labels. Put detailed
request context in sampled traces or structured logs instead.

Use rates/increases for counters, current values for population gauges, and
merged histogram buckets for cluster latency percentiles. Membership lifecycle
is an event counter, not a gauge of currently active nodes. RPC `response`
outcomes include non-success response statuses; chart the status attribute as
well as transport/dispatch failures.

| Alert condition | Investigate |
| --- | --- |
| A node stays unready or restarts repeatedly | Probe results, supervisor logs, Membership authority and configuration |
| Membership Table failure rate or latency stays above the deployment baseline | Provider connectivity, availability, and saturation |
| Directory failures, proof failures, or recovery latency persist after a rollout | Membership view, node incarnation, Directory transitions, related traces |
| Mailbox queues grow with RPC latency or notification rejection | Admission budgets, overloaded actors, slow consumers |
| Expected instance telemetry disappears or Collector export fails | Process health and the collection path independently |

Choose thresholds and sustained windows from load tests and service SLOs;
there is no universal Lakona threshold. Validate the notification route with
a controlled failure. Scrape `up` for the Collector only establishes that
Prometheus can reach the exporter; it does not establish that every game
process is healthy or still sending data. Exporters can retain stale samples
until expiration, so pair missing-data alerts with independent node probes.

## Verify and Troubleshoot

1. Start the Collector and confirm Prometheus shows its scrape target as UP.
2. Start each instrumented Lakona process with its own resource identity.
   Generate real client traffic, Actor calls, and session activity. Allow at
   least one SDK metric export interval plus a scrape interval; the .NET OTLP
   metric export interval defaults to 60 seconds. For a local smoke check,
   `OTEL_METRIC_EXPORT_INTERVAL=5000` shortens it to five seconds.
3. Inspect `http://otel-collector:9464/metrics` from the monitoring network.
   Confirm Lakona metrics from each expected `job`/`instance`, then confirm
   they appear in Grafana. Idle or role-specific counters may not exist yet.
4. Check Collector debug output for logs and sampled Actor/Cluster activities
   from the exercised paths. In production, verify the same records in the
   configured durable backends, not just that the Collector accepted them.
5. Check `/_lakona/health/ready` independently on each node through its allowed
   management interface. See [Configuration](./configuration.md#validation)
   for listener and loopback restrictions; OTLP does not require admin routes.

| Symptom | Check |
| --- | --- |
| No telemetry at all | SDK registrations in the running stable host; process environment; Collector DNS, firewall, port and protocol |
| Runtime metrics arrive but Lakona metrics do not | `AddMeter` uses the catalog above; traffic exercises the relevant role |
| Metrics work but traces do not | `AddSource`, trace sampling, activity-producing work, and the Collector traces pipeline |
| Logs missing | `AddOpenTelemetry` logging provider, application log-level filters, and the logs pipeline; see [Logging](./logging.md) |
| Nodes merge or disappear from panels | Unique resource instance ids, exporter labels, scrape label preservation, and dashboard filters |
| Readiness works but charts are empty | Probe success is independent of SDK export; inspect each hop from process to backend |

The [OTLP .NET exporter configuration](https://github.com/open-telemetry/opentelemetry-dotnet/blob/main/src/OpenTelemetry.Exporter.OpenTelemetryProtocol/README.md)
documents export intervals and environment variables. Keep production sampling
and log levels explicit so telemetry volume remains bounded.

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
