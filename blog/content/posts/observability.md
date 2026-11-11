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

Install these packages in the stable application host, not the reloadable
Hotfix project. From a generated project's root:

```bash
dotnet add Server/App/Server.App.csproj package OpenTelemetry.Extensions.Hosting
dotnet add Server/App/Server.App.csproj package OpenTelemetry.Exporter.OpenTelemetryProtocol
dotnet add Server/App/Server.App.csproj package OpenTelemetry.Instrumentation.Runtime
dotnet add Server/App/Server.App.csproj package OpenTelemetry.Instrumentation.AspNetCore
```

Pin compatible versions in your application's dependency management. Add the
following `using` directives to `Server/App/Program.cs`, then put the registrations
inside its existing `LakonaGameServer.RunAsync` configuration callback, before
the host starts. Here `server` is that callback's `LakonaGameServerBuilder`;
preserve existing transport, serializer, Membership Adapter, and application
registrations.

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

Set these variables in the server process environment, using your Collector's
address:

```text
OTEL_SERVICE_NAME=game-gateway
OTEL_EXPORTER_OTLP_ENDPOINT=http://otel-collector:4317
OTEL_EXPORTER_OTLP_PROTOCOL=grpc
```

For example, use `export` in Bash or `$env:OTEL_SERVICE_NAME = 'game-gateway'`
in PowerShell. The SDK's default resource uses this service identity for all
three signals. Environment variables alone do not register the SDK or subscribe
it to Lakona's meters and activity sources.

The Collector can then route the same signals to
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
