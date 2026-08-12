---
title: Use Lakona Observability
description: Connect Lakona metrics, traces, and logs to any OpenTelemetry-compatible stack.
date: 2026-08-11T00:00:00+08:00
---

Lakona uses the standard .NET telemetry primitives: `Meter`, `ActivitySource`,
and `ILogger`. The application owns the OpenTelemetry SDK, Collector, exporters,
dashboards, and alerts. There is no Lakona-specific telemetry protocol to deploy
or learn.

## Connect OpenTelemetry

Subscribe to the instrumentation scopes published by
`LakonaGameServerTelemetry`:

```csharp
using Lakona.Game.Server.Observability;
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
```

Point the exporter at the deployment's Collector with standard `OTEL_*`
environment variables. The Collector can then route the same signals to
Prometheus, Grafana, Tempo, Jaeger, Loki, Application Insights, or a hosted
OpenTelemetry backend.

## What To Monitor

Start with host CPU, memory, GC, process restarts, and network throughput from
standard runtime/process/host instrumentation. Add Lakona's low-cardinality
application metrics:

- actor activation population and mailbox queue length
- active, disconnected, and resumable sessions
- cluster route drops, expiry, and backpressure
- RPC request rate, response status, and dispatch duration
- timer capacity rejections
- reliable-push continuity loss

Keep actor ids, session ids, and request ids out of metric labels. Use sampled
traces and structured logs when individual-request detail is needed.

## Health Is A Separate Probe

Telemetry export can be delayed or unavailable without making a process dead.
Lakona therefore keeps orchestration probes as HTTP endpoints:

```text
GET /_lakona/health/live
GET /_lakona/health/ready
```

Configure those endpoints under `Lakona:Health` and the shared listener under
`Lakona:Management:Http`. Hotfix admin access, if enabled, is configured under
`Lakona:Management:Admin`.

Lakona no longer exposes `/_lakona/diagnostics/*` or owns a Prometheus/trace
export switch. For the complete source catalog, setup example, and multi-node
guidance, see the repository's
[Observability guide](https://github.com/bruce48x/Lakona/blob/main/docs/observability.md).
