# Core Observability Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build Lakona's core observability baseline: configuration, guardrails, local admin routing, safe diagnostics endpoints, diagnostics event buffering, and sanitized metric/trace instrumentation.

**Architecture:** Core runtime packages continue to use standard .NET diagnostics (`ILogger`, `Meter`, `ActivitySource`). `Lakona.Game.Server` owns `Lakona:Observability`, runtime validation, the unified local admin host, diagnostics snapshot aggregation, and the host-level event buffer. Optional Serilog/OpenTelemetry packages are not implemented in this plan; this plan adds explicit capability markers and validation so enabling missing integrations fails clearly.

**Tech Stack:** .NET 10, `Microsoft.Extensions.Hosting`, `Microsoft.Extensions.Logging`, `System.Diagnostics.Metrics`, `System.Diagnostics.Activity`, `HttpListener`, xUnit v3.

---

## Scope

This plan implements the core first slice from
`docs/superpowers/specs/2026-06-30-observability-design.md`.

Included:

- `Lakona:Observability` option binding and defaults.
- Runtime-profile aware local admin defaults.
- Observability guardrails with stable diagnostics.
- Unified `LakonaLocalAdminHost` replacing the hotfix-only hosted listener.
- Hotfix route preservation under the local admin host.
- Production hotfix package mode decoupled from local admin listener enablement.
- Bounded diagnostics event buffer.
- Safe diagnostics snapshot providers and JSON routes.
- Actor trace tag sanitization.
- Core tests and durable docs updates.
- Version bump for modified shippable packages.

Deferred to separate follow-up plans:

- `Lakona.Game.Server.Observability.Serilog` implementation.
- `Lakona.Game.Server.Observability.OpenTelemetry` implementation.
- A richer Prometheus endpoint package if the minimal core endpoint is not
  enough.
- Full transport/RPC connection accounting for a skynet-style `netstat`
  endpoint. This core plan creates the safe local route and response contract,
  but does not claim network counters until the RPC transport boundary exposes a
  stable diagnostics snapshot.

This keeps the first implementation shippable and testable without making core
Lakona depend on third-party exporter stacks.

## Design Coverage Matrix

| Design requirement | Current plan coverage |
| --- | --- |
| `Lakona:Observability` configuration and profile-aware defaults | Tasks 1, 2, 9, and 10 implement binding, validation, startup, and readiness. |
| Console logging enablement, category filters, and minimum levels | Task 9 configures `Microsoft.Extensions.Logging` from `Lakona:Observability:Logging`. |
| File logging, rotation, retention, and size limits | Task 2 validates `Logging:File` and fails with `ULINK133` unless a file logging integration is registered. Actual Serilog sink implementation belongs in `docs/superpowers/plans/2026-07-01-serilog-observability.md`. |
| Unified local admin host and hotfix route migration | Tasks 3 and 4 replace the hotfix-only listener and decouple production hotfix package loading from listener enablement. |
| Process, actor, session, and hotfix summary diagnostics | Tasks 6 and 7 add safe aggregate snapshots and local diagnostics routes. |
| RPC and transport netstat counters | Current plan adds the safe route contract and returns `status = "unavailable"` until `docs/superpowers/plans/2026-07-01-rpc-transport-observability.md` adds RPC/transport-owned counters. |
| Cluster diagnostics | Existing cluster diagnostics tests are preserved in Task 12. New cluster snapshot providers belong in `docs/superpowers/plans/2026-07-01-cluster-observability.md`. |
| Recent diagnostics event buffer | Task 5 adds the bounded buffer, captures sanitized Lakona warning/error logs, and bridges actor dead letter, slow message, and call timeout diagnostics. Task 7 records diagnostics provider failures. |
| Metrics instrumentation beyond existing actor/cluster counters | Task 8 sanitizes actor meter naming and tags. Additional RPC, transport, session, hotfix, and cluster meters belong in the subsystem follow-up plans listed in this table. |
| Distributed tracing export | Task 2 and Task 10 validate export configuration and fail with `ULINK134` without an integration. Actual OpenTelemetry wiring belongs in `docs/superpowers/plans/2026-07-01-opentelemetry-observability.md`. |

## Diagnostic Codes

Reserve these observability codes in `docs/guardrails.md` and tests:

```txt
ULINK130 error Observability local admin host must bind to loopback.
ULINK131 warning Diagnostics detail mode is enabled.
ULINK132 error Diagnostics detail mode cannot be exposed on non-loopback local admin.
ULINK133 error File logging requires a registered file logging integration.
ULINK134 error Tracing export requires a registered OpenTelemetry integration.
ULINK135 error Prometheus metrics endpoint requires a registered endpoint implementation.
ULINK136 error Observability metrics path is invalid.
ULINK137 error Observability event buffer capacity is invalid.
ULINK138 error Observability log level is invalid.
ULINK139 error Observability trace sample rate is invalid.
```

## File Structure

Create:

- `src/Lakona.Game.Server/Observability/LakonaObservabilityOptions.cs`
  Option model and binding for `Lakona:Observability`.
- `src/Lakona.Game.Server/Observability/LakonaObservabilityCapabilities.cs`
  DI-visible marker for optional integrations registered by future packages.
- `src/Lakona.Game.Server/Observability/LakonaObservabilityServiceCollectionExtensions.cs`
  Core registration for options, capabilities, diagnostics, local admin routes,
  and hosted service.
- `src/Lakona.Game.Server/Guardrails/LakonaGameResolvedObservability.cs`
  Resolved observability state consumed by guardrail rules.
- `src/Lakona.Game.Server/Guardrails/Rules/ObservabilityRule.cs`
  Runtime validation rules for unsafe/malformed observability config.
- `src/Lakona.Game.Server/Hosting/LakonaGameRuntimeProfileResolver.cs`
  Converts `Lakona:Profile` and host environment name into
  `LakonaGameRuntimeProfile`.
- `src/Lakona.Game.Server/LocalAdmin/ILakonaLocalAdminRoute.cs`
  Route contract for local admin modules.
- `src/Lakona.Game.Server/LocalAdmin/LakonaLocalAdminHostedService.cs`
  Single `HttpListener` host for all `/_lakona/*` routes.
- `src/Lakona.Game.Server/LocalAdmin/LakonaLocalAdminRouter.cs`
  Testable method/path router that does not require a live listener.
- `src/Lakona.Game.Server/LocalAdmin/LakonaLocalAdminRequest.cs`
  Bounded request abstraction for route handlers.
- `src/Lakona.Game.Server/LocalAdmin/LakonaLocalAdminResponse.cs`
  Status/content abstraction for route handlers.
- `src/Lakona.Game.Server/HotfixAdmin/HotfixAdminRoutes.cs`
  Hotfix route module for status/activate/rollback/reload.
- `src/Lakona.Game.Server/Observability/Diagnostics/DiagnosticsEvent.cs`
  Sanitized event DTO stored by the event buffer.
- `src/Lakona.Game.Server/Observability/Diagnostics/IDiagnosticsEventSink.cs`
- `src/Lakona.Game.Server/Observability/Diagnostics/BoundedDiagnosticsEventBuffer.cs`
- `src/Lakona.Game.Server/Observability/Diagnostics/DiagnosticsEventLoggerProvider.cs`
  Captures Lakona framework warning/error logs into sanitized event-buffer
  entries without rendering structured values.
- `src/Lakona.Game.Server/Observability/Diagnostics/ActorDiagnosticsEventBridge.cs`
  Converts actor diagnostics callbacks into sanitized event-buffer entries.
- `src/Lakona.Game.Server/Observability/Diagnostics/ILakonaDiagnosticsSnapshotProvider.cs`
- `src/Lakona.Game.Server/Observability/Diagnostics/LakonaDiagnosticsSnapshotService.cs`
- `src/Lakona.Game.Server/Observability/Diagnostics/DiagnosticsSnapshotModels.cs`
- `src/Lakona.Game.Server/Observability/Diagnostics/ProcessDiagnosticsProvider.cs`
- `src/Lakona.Game.Server/Observability/Diagnostics/ActorDiagnosticsProvider.cs`
- `src/Lakona.Game.Server/Observability/Diagnostics/SessionDiagnosticsProvider.cs`
- `src/Lakona.Game.Server/Observability/Diagnostics/HotfixDiagnosticsProvider.cs`
- `src/Lakona.Game.Server/Observability/Diagnostics/DiagnosticsLocalAdminRoutes.cs`
- `tests/Lakona.Game.Server.Tests/Observability/LakonaObservabilityOptionsTests.cs`
- `tests/Lakona.Game.Server.Tests/Observability/ObservabilityGuardrailTests.cs`
- `tests/Lakona.Game.Server.Tests/LocalAdmin/LakonaLocalAdminRouterTests.cs`
- `tests/Lakona.Game.Server.Tests/Observability/DiagnosticsEventBufferTests.cs`
- `tests/Lakona.Game.Server.Tests/Observability/DiagnosticsEndpointTests.cs`
- `tests/Lakona.Game.Server.Tests/Observability/ActorTraceSanitizationTests.cs`

Modify:

- `src/Lakona.Game.Server/Configuration/LakonaGameRuntimeOptions.cs`
  Add `Profile` and `Observability`.
- `src/Lakona.Game.Server/Configuration/LakonaGameHostingOptions.cs`
  Include observability logging settings if needed by startup logging setup.
- `src/Lakona.Game.Server/Guardrails/LakonaGameResolvedRuntime.cs`
  Add `Observability`.
- `src/Lakona.Game.Server/Guardrails/LakonaGameGuardrailServiceCollectionExtensions.cs`
  Register `ObservabilityRule`.
- `src/Lakona.Game.Server/Health/LakonaGameReadinessProbe.cs`
  Resolve profile/observability and include observability diagnostics in text
  and JSON output.
- `src/Lakona.Game.Server/Hosting/LakonaGameServer.cs`
  Resolve profile from host environment, configure logging, configure
  observability, register local admin routes, and decouple hotfix source mode.
- `src/Lakona.Game.Server/HotfixAdmin/HotfixAdminOptions.cs`
  Remove listener responsibility from hotfix options or keep legacy properties
  only as compatibility input.
- `src/Lakona.Game.Server/HotfixAdmin/HotfixAdminServiceCollectionExtensions.cs`
  Register controller/store/routes instead of a hosted listener.
- `src/Lakona.Game.Server/HotfixAdmin/HotfixAdminHostedService.cs`
  Delete after routes move to `LakonaLocalAdminHostedService`.
- `src/Lakona.Game.Server/Actors/IActorRuntime.cs`
  Add aggregate diagnostics method.
- `src/Lakona.Game.Server/Actors/IActorDiagnosticsObserver.cs`
  Add a low-level observer hook for sanitized host-side diagnostics bridges.
- `src/Lakona.Game.Server/Actors/LakonaActorRuntime.cs`
  Implement actor aggregate diagnostics and notify registered diagnostics
  observers.
- `src/Lakona.Game.Server/Sessions/IGameSessionRegistry.cs`
  Add safe aggregate diagnostics method.
- `src/Lakona.Game.Server/Sessions/InMemoryGameSessionRegistry.cs`
  Implement session aggregate diagnostics.
- `src/Lakona.Game.Server/Internal/ActorKernel/Diagnostics/LakonaActorDiagnostics.cs`
  Rename source/meter names to `Lakona.Game.Actor`.
- `src/Lakona.Game.Server/Internal/ActorKernel/Core/Dispatch/ActorTurnRunner.cs`
  Remove actor id and call chain tags.
- `src/Lakona.Game.Server/LakonaGameServerServiceCollectionExtensions.cs`
  Register core observability services.
- `src/Lakona.Game.Server/Lakona.Game.Server.csproj`
  Bump version.
- `Lakona.slnx` and `tests/Tests.slnx`
  Only if new test projects are introduced. This plan uses existing
  `Lakona.Game.Server.Tests`, so no solution changes should be needed.
- `docs/configuration.md`
- `docs/guardrails.md`
- `docs/actor.md`
- `docs/hotfix/architecture.md`
- `src/Lakona.Game.Server/README.md`

---

### Task 1: Add Observability Options and Profile Resolution

**Files:**
- Create: `src/Lakona.Game.Server/Observability/LakonaObservabilityOptions.cs`
- Create: `src/Lakona.Game.Server/Observability/LakonaObservabilityCapabilities.cs`
- Create: `src/Lakona.Game.Server/Hosting/LakonaGameRuntimeProfileResolver.cs`
- Modify: `src/Lakona.Game.Server/Configuration/LakonaGameRuntimeOptions.cs`
- Test: `tests/Lakona.Game.Server.Tests/Observability/LakonaObservabilityOptionsTests.cs`

- [ ] **Step 1: Write option binding tests**

Add `tests/Lakona.Game.Server.Tests/Observability/LakonaObservabilityOptionsTests.cs`:

```csharp
using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.Guardrails;
using Lakona.Game.Server.Hosting;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Lakona.Game.Server.Tests.Observability;

public sealed class LakonaObservabilityOptionsTests
{
    [Fact]
    public void Defaults_enable_local_admin_only_for_development_profile()
    {
        var configuration = new ConfigurationBuilder().Build();

        var development = LakonaObservabilityOptions.FromConfiguration(
            configuration,
            LakonaGameRuntimeProfile.Development);
        var production = LakonaObservabilityOptions.FromConfiguration(
            configuration,
            LakonaGameRuntimeProfile.Production);

        Assert.True(development.LocalAdmin.EffectiveEnabled);
        Assert.False(production.LocalAdmin.EffectiveEnabled);
        Assert.Equal("127.0.0.1", development.LocalAdmin.Host);
        Assert.Equal(20090, development.LocalAdmin.Port);
        Assert.False(development.Diagnostics.DetailEnabled);
        Assert.False(development.Metrics.Prometheus.Enabled);
        Assert.False(development.Tracing.Export.Enabled);
    }

    [Fact]
    public void Binds_logging_local_admin_diagnostics_metrics_and_tracing()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Lakona:Observability:Logging:Enabled"] = "false",
                ["Lakona:Observability:Logging:MinimumLevel"] = "Debug",
                ["Lakona:Observability:Logging:Categories:Lakona.Game.Actor"] = "Warning",
                ["Lakona:Observability:Logging:Console:Enabled"] = "false",
                ["Lakona:Observability:Logging:File:Enabled"] = "true",
                ["Lakona:Observability:Logging:File:Path"] = "logs/server-.log",
                ["Lakona:Observability:LocalAdmin:Enabled"] = "true",
                ["Lakona:Observability:LocalAdmin:Host"] = "localhost",
                ["Lakona:Observability:LocalAdmin:Port"] = "20100",
                ["Lakona:Observability:Diagnostics:DetailEnabled"] = "true",
                ["Lakona:Observability:Diagnostics:EventBuffer:Capacity"] = "32",
                ["Lakona:Observability:Metrics:Prometheus:Enabled"] = "true",
                ["Lakona:Observability:Metrics:Prometheus:Path"] = "/_lakona/metrics",
                ["Lakona:Observability:Tracing:Export:Enabled"] = "true",
                ["Lakona:Observability:Tracing:Export:SampleRate"] = "0.5"
            })
            .Build();

        var options = LakonaObservabilityOptions.FromConfiguration(
            configuration,
            LakonaGameRuntimeProfile.Production);

        Assert.False(options.Logging.Enabled);
        Assert.Equal("Debug", options.Logging.MinimumLevel);
        Assert.Equal("Warning", options.Logging.Categories["Lakona.Game.Actor"]);
        Assert.False(options.Logging.Console.Enabled);
        Assert.True(options.Logging.File.Enabled);
        Assert.Equal("logs/server-.log", options.Logging.File.Path);
        Assert.True(options.LocalAdmin.EffectiveEnabled);
        Assert.Equal("localhost", options.LocalAdmin.Host);
        Assert.Equal(20100, options.LocalAdmin.Port);
        Assert.True(options.Diagnostics.DetailEnabled);
        Assert.Equal(32, options.Diagnostics.EventBuffer.Capacity);
        Assert.True(options.Metrics.Prometheus.Enabled);
        Assert.True(options.Tracing.Export.Enabled);
        Assert.Equal(0.5, options.Tracing.Export.SampleRate);
    }

    [Theory]
    [InlineData("Development", LakonaGameRuntimeProfile.Development)]
    [InlineData("Compose", LakonaGameRuntimeProfile.Compose)]
    [InlineData("Production", LakonaGameRuntimeProfile.Production)]
    [InlineData("battle-1", LakonaGameRuntimeProfile.Production)]
    public void Profile_resolver_does_not_treat_node_environment_as_development(
        string environmentName,
        LakonaGameRuntimeProfile expected)
    {
        var configuration = new ConfigurationBuilder().Build();

        var profile = LakonaGameRuntimeProfileResolver.Resolve(configuration, environmentName);

        Assert.Equal(expected, profile);
    }

    [Fact]
    public void Explicit_lakona_profile_overrides_host_environment_name()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Lakona:Profile"] = "Compose"
            })
            .Build();

        var profile = LakonaGameRuntimeProfileResolver.Resolve(configuration, "battle-1");

        Assert.Equal(LakonaGameRuntimeProfile.Compose, profile);
    }

    [Fact]
    public void Observability_capabilities_aggregate_composable_markers()
    {
        var capabilities = LakonaObservabilityCapabilities.FromServices(
        [
            new FileLoggingObservabilityCapability(),
            new OpenTelemetryObservabilityCapability()
        ]);

        Assert.True(capabilities.FileLoggingIntegrationRegistered);
        Assert.True(capabilities.OpenTelemetryIntegrationRegistered);
        Assert.False(capabilities.PrometheusEndpointRegistered);
    }
}
```

- [ ] **Step 2: Run option tests and verify they fail**

Run:

```powershell
dotnet test tests\Lakona.Game.Server.Tests\Lakona.Game.Server.Tests.csproj --no-restore --filter FullyQualifiedName~LakonaObservabilityOptionsTests
```

Expected: compile fails because `LakonaObservabilityOptions` and
`LakonaGameRuntimeProfileResolver` do not exist.

- [ ] **Step 3: Implement options and capabilities**

Create `src/Lakona.Game.Server/Observability/LakonaObservabilityOptions.cs`:

```csharp
using Microsoft.Extensions.Configuration;
using Lakona.Game.Server.Guardrails;
using static Lakona.Game.Server.Configuration.ObservabilityOptionBinder;

namespace Lakona.Game.Server.Configuration;

public sealed record LakonaObservabilityOptions(
    LakonaObservabilityLoggingOptions Logging,
    LakonaObservabilityLocalAdminOptions LocalAdmin,
    LakonaObservabilityDiagnosticsOptions Diagnostics,
    LakonaObservabilityMetricsOptions Metrics,
    LakonaObservabilityTracingOptions Tracing)
{
    public static LakonaObservabilityOptions FromConfiguration(
        IConfiguration configuration,
        LakonaGameRuntimeProfile profile)
    {
        var section = configuration.GetSection("Lakona:Observability");
        var defaults = CreateDefaults(profile);
        return new LakonaObservabilityOptions(
            LakonaObservabilityLoggingOptions.FromConfiguration(section.GetSection("Logging"), defaults.Logging),
            LakonaObservabilityLocalAdminOptions.FromConfiguration(section.GetSection("LocalAdmin"), defaults.LocalAdmin, profile),
            LakonaObservabilityDiagnosticsOptions.FromConfiguration(section.GetSection("Diagnostics"), defaults.Diagnostics),
            LakonaObservabilityMetricsOptions.FromConfiguration(section.GetSection("Metrics"), defaults.Metrics),
            LakonaObservabilityTracingOptions.FromConfiguration(section.GetSection("Tracing"), defaults.Tracing));
    }

    private static LakonaObservabilityOptions CreateDefaults(LakonaGameRuntimeProfile profile) =>
        new(
            new LakonaObservabilityLoggingOptions(
                Enabled: true,
                MinimumLevel: "Information",
                Categories: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["Lakona.Rpc"] = "Information",
                    ["Lakona.Rpc.Transport"] = "Information",
                    ["Lakona.Game.Server"] = "Information",
                    ["Lakona.Game.Session"] = "Information",
                    ["Lakona.Game.Actor"] = "Information",
                    ["Lakona.Game.Cluster"] = "Information",
                    ["Lakona.Game.Hotfix"] = "Information",
                    ["Lakona.Game.Observability"] = "Information"
                },
                Console: new LakonaObservabilityConsoleLoggingOptions(true, "Compact", false),
                File: new LakonaObservabilityFileLoggingOptions(false, "logs/lakona-.log", "Day", 7, 128)),
            new LakonaObservabilityLocalAdminOptions(
                Enabled: null,
                EffectiveEnabled: profile == LakonaGameRuntimeProfile.Development,
                Host: "127.0.0.1",
                Port: 20090,
                RequireLoopback: true),
            new LakonaObservabilityDiagnosticsOptions(
                SummaryEnabled: true,
                DetailEnabled: false,
                EventBuffer: new LakonaObservabilityEventBufferOptions(true, 1024, "Warning")),
            new LakonaObservabilityMetricsOptions(
                Prometheus: new LakonaObservabilityPrometheusOptions(false, "/_lakona/metrics")),
            new LakonaObservabilityTracingOptions(
                Export: new LakonaObservabilityTraceExportOptions(false, 1.0)));
}

public sealed record LakonaObservabilityLoggingOptions(
    bool Enabled,
    string MinimumLevel,
    IReadOnlyDictionary<string, string> Categories,
    LakonaObservabilityConsoleLoggingOptions Console,
    LakonaObservabilityFileLoggingOptions File)
{
    public static LakonaObservabilityLoggingOptions FromConfiguration(
        IConfigurationSection section,
        LakonaObservabilityLoggingOptions defaults)
    {
        var categories = new Dictionary<string, string>(defaults.Categories, StringComparer.Ordinal);
        foreach (var category in section.GetSection("Categories").GetChildren())
        {
            categories[category.Key] = category.Value ?? "";
        }

        return defaults with
        {
            Enabled = ReadBool(section, "Enabled", defaults.Enabled),
            MinimumLevel = ReadString(section, "MinimumLevel", defaults.MinimumLevel),
            Categories = categories,
            Console = LakonaObservabilityConsoleLoggingOptions.FromConfiguration(section.GetSection("Console"), defaults.Console),
            File = LakonaObservabilityFileLoggingOptions.FromConfiguration(section.GetSection("File"), defaults.File)
        };
    }
}

public sealed record LakonaObservabilityConsoleLoggingOptions(
    bool Enabled,
    string Format,
    bool IncludeScopes)
{
    public static LakonaObservabilityConsoleLoggingOptions FromConfiguration(
        IConfigurationSection section,
        LakonaObservabilityConsoleLoggingOptions defaults) =>
        defaults with
        {
            Enabled = ReadBool(section, "Enabled", defaults.Enabled),
            Format = ReadString(section, "Format", defaults.Format),
            IncludeScopes = ReadBool(section, "IncludeScopes", defaults.IncludeScopes)
        };
}

public sealed record LakonaObservabilityFileLoggingOptions(
    bool Enabled,
    string Path,
    string RollingInterval,
    int RetainedFileCount,
    int FileSizeLimitMB)
{
    public static LakonaObservabilityFileLoggingOptions FromConfiguration(
        IConfigurationSection section,
        LakonaObservabilityFileLoggingOptions defaults) =>
        defaults with
        {
            Enabled = ReadBool(section, "Enabled", defaults.Enabled),
            Path = ReadString(section, "Path", defaults.Path),
            RollingInterval = ReadString(section, "RollingInterval", defaults.RollingInterval),
            RetainedFileCount = ReadInt(section, "RetainedFileCount", defaults.RetainedFileCount),
            FileSizeLimitMB = ReadInt(section, "FileSizeLimitMB", defaults.FileSizeLimitMB)
        };
}

public sealed record LakonaObservabilityLocalAdminOptions(
    bool? Enabled,
    bool EffectiveEnabled,
    string Host,
    int Port,
    bool RequireLoopback)
{
    public static LakonaObservabilityLocalAdminOptions FromConfiguration(
        IConfigurationSection section,
        LakonaObservabilityLocalAdminOptions defaults,
        LakonaGameRuntimeProfile profile)
    {
        bool? configuredEnabled = bool.TryParse(section["Enabled"], out var enabled) ? enabled : null;
        bool defaultEnabled = profile == LakonaGameRuntimeProfile.Development;
        return defaults with
        {
            Enabled = configuredEnabled,
            EffectiveEnabled = configuredEnabled ?? defaultEnabled,
            Host = ReadString(section, "Host", defaults.Host),
            Port = ReadInt(section, "Port", defaults.Port),
            RequireLoopback = ReadBool(section, "RequireLoopback", defaults.RequireLoopback)
        };
    }
}

public sealed record LakonaObservabilityDiagnosticsOptions(
    bool SummaryEnabled,
    bool DetailEnabled,
    LakonaObservabilityEventBufferOptions EventBuffer)
{
    public static LakonaObservabilityDiagnosticsOptions FromConfiguration(
        IConfigurationSection section,
        LakonaObservabilityDiagnosticsOptions defaults) =>
        defaults with
        {
            SummaryEnabled = ReadBool(section, "SummaryEnabled", defaults.SummaryEnabled),
            DetailEnabled = ReadBool(section, "DetailEnabled", defaults.DetailEnabled),
            EventBuffer = LakonaObservabilityEventBufferOptions.FromConfiguration(section.GetSection("EventBuffer"), defaults.EventBuffer)
        };
}

public sealed record LakonaObservabilityEventBufferOptions(
    bool Enabled,
    int Capacity,
    string MinimumLevel)
{
    public static LakonaObservabilityEventBufferOptions FromConfiguration(
        IConfigurationSection section,
        LakonaObservabilityEventBufferOptions defaults) =>
        defaults with
        {
            Enabled = ReadBool(section, "Enabled", defaults.Enabled),
            Capacity = ReadInt(section, "Capacity", defaults.Capacity),
            MinimumLevel = ReadString(section, "MinimumLevel", defaults.MinimumLevel)
        };
}

public sealed record LakonaObservabilityMetricsOptions(
    LakonaObservabilityPrometheusOptions Prometheus)
{
    public static LakonaObservabilityMetricsOptions FromConfiguration(
        IConfigurationSection section,
        LakonaObservabilityMetricsOptions defaults) =>
        defaults with
        {
            Prometheus = LakonaObservabilityPrometheusOptions.FromConfiguration(section.GetSection("Prometheus"), defaults.Prometheus)
        };
}

public sealed record LakonaObservabilityPrometheusOptions(
    bool Enabled,
    string Path)
{
    public static LakonaObservabilityPrometheusOptions FromConfiguration(
        IConfigurationSection section,
        LakonaObservabilityPrometheusOptions defaults) =>
        defaults with
        {
            Enabled = ReadBool(section, "Enabled", defaults.Enabled),
            Path = ReadString(section, "Path", defaults.Path)
        };
}

public sealed record LakonaObservabilityTracingOptions(
    LakonaObservabilityTraceExportOptions Export)
{
    public static LakonaObservabilityTracingOptions FromConfiguration(
        IConfigurationSection section,
        LakonaObservabilityTracingOptions defaults) =>
        defaults with
        {
            Export = LakonaObservabilityTraceExportOptions.FromConfiguration(section.GetSection("Export"), defaults.Export)
        };
}

public sealed record LakonaObservabilityTraceExportOptions(
    bool Enabled,
    double SampleRate)
{
    public static LakonaObservabilityTraceExportOptions FromConfiguration(
        IConfigurationSection section,
        LakonaObservabilityTraceExportOptions defaults) =>
        defaults with
        {
            Enabled = ReadBool(section, "Enabled", defaults.Enabled),
            SampleRate = ReadDouble(section, "SampleRate", defaults.SampleRate)
        };
}

internal static class ObservabilityOptionBinder
{
    public static bool ReadBool(IConfiguration section, string key, bool fallback) =>
        bool.TryParse(section[key], out var value) ? value : fallback;

    public static string ReadString(IConfiguration section, string key, string fallback) =>
        string.IsNullOrWhiteSpace(section[key]) ? fallback : section[key]!;

    public static int ReadInt(IConfiguration section, string key, int fallback) =>
        int.TryParse(section[key], out var value) ? value : fallback;

    public static double ReadDouble(IConfiguration section, string key, double fallback) =>
        double.TryParse(section[key], out var value) ? value : fallback;
}
```

Create `src/Lakona.Game.Server/Observability/LakonaObservabilityCapabilities.cs`:

```csharp
namespace Lakona.Game.Server.Configuration;

public interface ILakonaObservabilityCapability
{
    LakonaObservabilityCapabilityKind Kind { get; }
}

public enum LakonaObservabilityCapabilityKind
{
    FileLogging = 0,
    OpenTelemetry = 1,
    PrometheusEndpoint = 2
}

public sealed record LakonaObservabilityCapabilities(
    bool FileLoggingIntegrationRegistered = false,
    bool OpenTelemetryIntegrationRegistered = false,
    bool PrometheusEndpointRegistered = false)
{
    public static LakonaObservabilityCapabilities FromServices(
        IEnumerable<ILakonaObservabilityCapability> capabilities)
    {
        var kinds = capabilities
            .Select(capability => capability.Kind)
            .ToHashSet();
        return new LakonaObservabilityCapabilities(
            FileLoggingIntegrationRegistered: kinds.Contains(LakonaObservabilityCapabilityKind.FileLogging),
            OpenTelemetryIntegrationRegistered: kinds.Contains(LakonaObservabilityCapabilityKind.OpenTelemetry),
            PrometheusEndpointRegistered: kinds.Contains(LakonaObservabilityCapabilityKind.PrometheusEndpoint));
    }
}

public sealed class FileLoggingObservabilityCapability : ILakonaObservabilityCapability
{
    public LakonaObservabilityCapabilityKind Kind => LakonaObservabilityCapabilityKind.FileLogging;
}

public sealed class OpenTelemetryObservabilityCapability : ILakonaObservabilityCapability
{
    public LakonaObservabilityCapabilityKind Kind => LakonaObservabilityCapabilityKind.OpenTelemetry;
}

public sealed class PrometheusEndpointObservabilityCapability : ILakonaObservabilityCapability
{
    public LakonaObservabilityCapabilityKind Kind => LakonaObservabilityCapabilityKind.PrometheusEndpoint;
}
```

- [ ] **Step 4: Implement runtime profile resolution**

Create `src/Lakona.Game.Server/Hosting/LakonaGameRuntimeProfileResolver.cs`:

```csharp
using Microsoft.Extensions.Configuration;
using Lakona.Game.Server.Guardrails;

namespace Lakona.Game.Server.Hosting;

internal static class LakonaGameRuntimeProfileResolver
{
    public static LakonaGameRuntimeProfile Resolve(
        IConfiguration configuration,
        string? environmentName)
    {
        var configured = configuration["Lakona:Profile"];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Enum.TryParse<LakonaGameRuntimeProfile>(configured, ignoreCase: true, out var parsed)
                ? parsed
                : throw new InvalidOperationException(
                    $"Lakona:Profile '{configured}' is unknown. Use Development, Compose, or Production.");
        }

        if (string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase))
        {
            return LakonaGameRuntimeProfile.Development;
        }

        if (string.Equals(environmentName, "Compose", StringComparison.OrdinalIgnoreCase))
        {
            return LakonaGameRuntimeProfile.Compose;
        }

        if (string.Equals(environmentName, "Production", StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrWhiteSpace(environmentName))
        {
            return LakonaGameRuntimeProfile.Production;
        }

        return LakonaGameRuntimeProfile.Development;
    }
}
```

- [ ] **Step 5: Carry profile and observability in runtime options**

Modify `src/Lakona.Game.Server/Configuration/LakonaGameRuntimeOptions.cs`:

```csharp
public sealed class LakonaGameRuntimeOptions
{
    public LakonaGameRuntimeProfile Profile { get; init; } = LakonaGameRuntimeProfile.Development;
    public LakonaObservabilityOptions Observability { get; init; } =
        LakonaObservabilityOptions.FromConfiguration(
            new ConfigurationBuilder().Build(),
            LakonaGameRuntimeProfile.Development);

    public static LakonaGameRuntimeOptions FromConfiguration(
        IConfiguration configuration,
        string? environmentName = null)
    {
        var profile = LakonaGameRuntimeProfileResolver.Resolve(configuration, environmentName);
        var section = GetRuntimeSection(configuration);

        return new LakonaGameRuntimeOptions
        {
            Profile = profile,
            Observability = LakonaObservabilityOptions.FromConfiguration(configuration, profile),
            Node = BindNode(section.GetSection("Node")),
            Endpoints = BindEndpoints(section.GetSection("Endpoints")),
            Feature = BindOptionalStringArray(section.GetSection("Feature")),
            Cluster = BindCluster(section.GetSection("Cluster"))
        };
    }
}
```

Add `using Lakona.Game.Server.Guardrails;` and
`using Lakona.Game.Server.Hosting;`.

- [ ] **Step 6: Run option tests**

Run:

```powershell
dotnet test tests\Lakona.Game.Server.Tests\Lakona.Game.Server.Tests.csproj --no-restore --filter FullyQualifiedName~LakonaObservabilityOptionsTests
```

Expected: all `LakonaObservabilityOptionsTests` pass.

- [ ] **Step 7: Commit**

```powershell
git add src/Lakona.Game.Server/Observability/LakonaObservabilityOptions.cs src/Lakona.Game.Server/Observability/LakonaObservabilityCapabilities.cs src/Lakona.Game.Server/Hosting/LakonaGameRuntimeProfileResolver.cs src/Lakona.Game.Server/Configuration/LakonaGameRuntimeOptions.cs tests/Lakona.Game.Server.Tests/Observability/LakonaObservabilityOptionsTests.cs
git commit -m "Add observability options"
```

---

### Task 2: Add Observability Guardrails and Readiness Output

**Files:**
- Create: `src/Lakona.Game.Server/Guardrails/LakonaGameResolvedObservability.cs`
- Create: `src/Lakona.Game.Server/Guardrails/Rules/ObservabilityRule.cs`
- Modify: `src/Lakona.Game.Server/Guardrails/LakonaGameResolvedRuntime.cs`
- Modify: `src/Lakona.Game.Server/Guardrails/LakonaGameGuardrailServiceCollectionExtensions.cs`
- Modify: `src/Lakona.Game.Server/Health/LakonaGameReadinessProbe.cs`
- Test: `tests/Lakona.Game.Server.Tests/Observability/ObservabilityGuardrailTests.cs`
- Test: `tests/Lakona.Game.Server.Tests/Health/LakonaGameReadinessProbeTests.cs`

- [ ] **Step 1: Write guardrail tests**

Add `tests/Lakona.Game.Server.Tests/Observability/ObservabilityGuardrailTests.cs`:

```csharp
using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.Guardrails;
using Lakona.Game.Server.Guardrails.Rules;
using Xunit;

namespace Lakona.Game.Server.Tests.Observability;

public sealed class ObservabilityGuardrailTests
{
    [Fact]
    public void Rejects_non_loopback_local_admin_when_enabled()
    {
        var result = Validate(Runtime(observability: Observability(localAdminHost: "10.0.0.5", localAdminEnabled: true)));

        var diagnostic = Assert.Single(result.Diagnostics, d => d.Code == "ULINK130");
        Assert.Equal(LakonaGameDiagnosticSeverity.Error, diagnostic.Severity);
    }

    [Fact]
    public void Warns_when_detail_diagnostics_are_enabled_on_loopback()
    {
        var result = Validate(Runtime(observability: Observability(detailEnabled: true)));

        var diagnostic = Assert.Single(result.Diagnostics, d => d.Code == "ULINK131");
        Assert.Equal(LakonaGameDiagnosticSeverity.Warning, diagnostic.Severity);
    }

    [Fact]
    public void Rejects_detail_diagnostics_on_non_loopback()
    {
        var result = Validate(Runtime(observability: Observability(
            localAdminHost: "10.0.0.5",
            localAdminEnabled: true,
            detailEnabled: true)));

        Assert.Contains(result.Diagnostics, d => d.Code == "ULINK130");
        Assert.Contains(result.Diagnostics, d => d.Code == "ULINK132");
    }

    [Theory]
    [InlineData("File", "ULINK133")]
    [InlineData("Tracing", "ULINK134")]
    [InlineData("Prometheus", "ULINK135")]
    public void Rejects_enabled_exporter_without_registered_capability(string exporter, string code)
    {
        var observability = exporter switch
        {
            "File" => Observability(fileEnabled: true),
            "Tracing" => Observability(traceExportEnabled: true),
            "Prometheus" => Observability(prometheusEnabled: true),
            _ => throw new ArgumentOutOfRangeException(nameof(exporter))
        };

        var result = Validate(Runtime(observability: observability));

        Assert.Contains(result.Diagnostics, d => d.Code == code);
    }

    [Theory]
    [InlineData("", "ULINK136")]
    [InlineData("metrics", "ULINK136")]
    [InlineData("/_lakona/metrics?bad=true", "ULINK136")]
    public void Rejects_invalid_prometheus_path(string path, string code)
    {
        var result = Validate(Runtime(observability: Observability(prometheusPath: path)));

        Assert.Contains(result.Diagnostics, d => d.Code == code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Rejects_invalid_event_buffer_capacity(int capacity)
    {
        var result = Validate(Runtime(observability: Observability(eventBufferCapacity: capacity)));

        Assert.Contains(result.Diagnostics, d => d.Code == "ULINK137");
    }

    [Theory]
    [InlineData("Verbose")]
    [InlineData("")]
    public void Rejects_invalid_log_level(string level)
    {
        var result = Validate(Runtime(observability: Observability(minimumLevel: level)));

        Assert.Contains(result.Diagnostics, d => d.Code == "ULINK138");
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public void Rejects_invalid_trace_sample_rate(double sampleRate)
    {
        var result = Validate(Runtime(observability: Observability(sampleRate: sampleRate)));

        Assert.Contains(result.Diagnostics, d => d.Code == "ULINK139");
    }

    private static LakonaGameValidationResult Validate(LakonaGameResolvedRuntime runtime) =>
        new LakonaGameRuntimeValidator([new ObservabilityRule()]).Validate(runtime);

    private static LakonaGameResolvedRuntime Runtime(LakonaGameResolvedObservability? observability = null) =>
        new(
            NodeId: new LakonaGameResolvedValue<string>("node-1", LakonaGameValueSource.Configuration),
            Endpoints: [],
            Cluster: new LakonaGameResolvedCluster(new Dictionary<string, string>()),
            ClusterEndpoint: null,
            Feature: new LakonaGameResolvedFeature(null, [], []),
            Hotfix: new LakonaGameResolvedHotfix(
                new LakonaGameResolvedValue<string>("Server.Hotfix.dll", LakonaGameValueSource.GeneratedConvention),
                new LakonaGameResolvedValue<string>("Server.Hotfix.dll", LakonaGameValueSource.GeneratedConvention)),
            ReliablePush: new LakonaGameResolvedReliablePush(
                new LakonaGameResolvedValue<string>("InMemory", LakonaGameValueSource.Default),
                new LakonaGameResolvedValue<int>(256, LakonaGameValueSource.Default),
                new LakonaGameResolvedValue<int>(120, LakonaGameValueSource.Default),
                HasSessionIdentityResolver: true),
            Observability: observability ?? Observability(),
            Profile: LakonaGameRuntimeProfile.Production);

    private static LakonaGameResolvedObservability Observability(
        string localAdminHost = "127.0.0.1",
        bool localAdminEnabled = true,
        bool detailEnabled = false,
        bool fileEnabled = false,
        bool traceExportEnabled = false,
        bool prometheusEnabled = false,
        string prometheusPath = "/_lakona/metrics",
        int eventBufferCapacity = 1024,
        string minimumLevel = "Information",
        double sampleRate = 1.0) =>
        new(
            LocalAdminEnabled: new LakonaGameResolvedValue<bool>(localAdminEnabled, LakonaGameValueSource.Configuration, "Lakona:Observability:LocalAdmin:Enabled"),
            LocalAdminHost: new LakonaGameResolvedValue<string>(localAdminHost, LakonaGameValueSource.Configuration, "Lakona:Observability:LocalAdmin:Host"),
            LocalAdminRequireLoopback: new LakonaGameResolvedValue<bool>(true, LakonaGameValueSource.Configuration, "Lakona:Observability:LocalAdmin:RequireLoopback"),
            DetailEnabled: new LakonaGameResolvedValue<bool>(detailEnabled, LakonaGameValueSource.Configuration, "Lakona:Observability:Diagnostics:DetailEnabled"),
            FileLoggingEnabled: new LakonaGameResolvedValue<bool>(fileEnabled, LakonaGameValueSource.Configuration, "Lakona:Observability:Logging:File:Enabled"),
            FileLoggingIntegrationRegistered: false,
            TraceExportEnabled: new LakonaGameResolvedValue<bool>(traceExportEnabled, LakonaGameValueSource.Configuration, "Lakona:Observability:Tracing:Export:Enabled"),
            OpenTelemetryIntegrationRegistered: false,
            PrometheusEnabled: new LakonaGameResolvedValue<bool>(prometheusEnabled, LakonaGameValueSource.Configuration, "Lakona:Observability:Metrics:Prometheus:Enabled"),
            PrometheusEndpointRegistered: false,
            PrometheusPath: new LakonaGameResolvedValue<string>(prometheusPath, LakonaGameValueSource.Configuration, "Lakona:Observability:Metrics:Prometheus:Path"),
            EventBufferCapacity: new LakonaGameResolvedValue<int>(eventBufferCapacity, LakonaGameValueSource.Configuration, "Lakona:Observability:Diagnostics:EventBuffer:Capacity"),
            LoggingMinimumLevel: new LakonaGameResolvedValue<string>(minimumLevel, LakonaGameValueSource.Configuration, "Lakona:Observability:Logging:MinimumLevel"),
            TraceSampleRate: new LakonaGameResolvedValue<double>(sampleRate, LakonaGameValueSource.Configuration, "Lakona:Observability:Tracing:Export:SampleRate"));
}
```

- [ ] **Step 2: Run guardrail tests and verify they fail**

Run:

```powershell
dotnet test tests\Lakona.Game.Server.Tests\Lakona.Game.Server.Tests.csproj --no-restore --filter FullyQualifiedName~ObservabilityGuardrailTests
```

Expected: compile fails because resolved observability and `ObservabilityRule`
do not exist.

- [ ] **Step 3: Add resolved observability model**

Create `src/Lakona.Game.Server/Guardrails/LakonaGameResolvedObservability.cs`:

```csharp
namespace Lakona.Game.Server.Guardrails;

public sealed record LakonaGameResolvedObservability(
    LakonaGameResolvedValue<bool> LocalAdminEnabled,
    LakonaGameResolvedValue<string> LocalAdminHost,
    LakonaGameResolvedValue<bool> LocalAdminRequireLoopback,
    LakonaGameResolvedValue<bool> DetailEnabled,
    LakonaGameResolvedValue<bool> FileLoggingEnabled,
    bool FileLoggingIntegrationRegistered,
    LakonaGameResolvedValue<bool> TraceExportEnabled,
    bool OpenTelemetryIntegrationRegistered,
    LakonaGameResolvedValue<bool> PrometheusEnabled,
    bool PrometheusEndpointRegistered,
    LakonaGameResolvedValue<string> PrometheusPath,
    LakonaGameResolvedValue<int> EventBufferCapacity,
    LakonaGameResolvedValue<string> LoggingMinimumLevel,
    LakonaGameResolvedValue<double> TraceSampleRate);
```

Modify `src/Lakona.Game.Server/Guardrails/LakonaGameResolvedRuntime.cs`:

```csharp
public sealed record LakonaGameResolvedRuntime(
    LakonaGameResolvedValue<string> NodeId,
    IReadOnlyList<LakonaGameResolvedEndpoint> Endpoints,
    LakonaGameResolvedCluster Cluster,
    LakonaGameResolvedClusterEndpoint? ClusterEndpoint,
    LakonaGameResolvedFeature Feature,
    LakonaGameResolvedHotfix Hotfix,
    LakonaGameResolvedReliablePush ReliablePush,
    LakonaGameResolvedObservability Observability,
    LakonaGameRuntimeProfile Profile);
```

Modify `tests/Lakona.Game.Server.Tests/Guardrails/LakonaGameRuntimeValidatorTests.cs`
so its `TestRuntime()` helper adds the new observability argument:

```csharp
private static LakonaGameResolvedRuntime TestRuntime()
{
    return new LakonaGameResolvedRuntime(
        NodeId: new LakonaGameResolvedValue<string>("dev-1", LakonaGameValueSource.Configuration, "Lakona:Node:Id"),
        Endpoints: [TestEndpoint("kcp", "127.0.0.1", 20000)],
        Cluster: new LakonaGameResolvedCluster(
            AdvertisedEndpoints: new Dictionary<string, string> { ["client"] = "kcp://127.0.0.1:20000" }),
        ClusterEndpoint: null,
        Feature: new LakonaGameResolvedFeature(
            Configured: null,
            Active: [],
            StartupOrder: []),
        Hotfix: new LakonaGameResolvedHotfix(
            AssemblyPath: new LakonaGameResolvedValue<string>("Server.Hotfix.dll", LakonaGameValueSource.GeneratedConvention),
            AssemblyFileName: new LakonaGameResolvedValue<string>("Server.Hotfix.dll", LakonaGameValueSource.GeneratedConvention)),
        ReliablePush: new LakonaGameResolvedReliablePush(
            StorageMode: new LakonaGameResolvedValue<string>("InMemory", LakonaGameValueSource.Default),
            PendingLimit: new LakonaGameResolvedValue<int>(256, LakonaGameValueSource.Default),
            ReplayWindowSeconds: new LakonaGameResolvedValue<int>(120, LakonaGameValueSource.Default),
            HasSessionIdentityResolver: true),
        Observability: Observability(),
        Profile: LakonaGameRuntimeProfile.Development);
}

private static LakonaGameResolvedObservability Observability() =>
    new(
        LocalAdminEnabled: new LakonaGameResolvedValue<bool>(false, LakonaGameValueSource.Default, "Lakona:Observability:LocalAdmin:Enabled"),
        LocalAdminHost: new LakonaGameResolvedValue<string>("127.0.0.1", LakonaGameValueSource.Default, "Lakona:Observability:LocalAdmin:Host"),
        LocalAdminRequireLoopback: new LakonaGameResolvedValue<bool>(true, LakonaGameValueSource.Default, "Lakona:Observability:LocalAdmin:RequireLoopback"),
        DetailEnabled: new LakonaGameResolvedValue<bool>(false, LakonaGameValueSource.Default, "Lakona:Observability:Diagnostics:DetailEnabled"),
        FileLoggingEnabled: new LakonaGameResolvedValue<bool>(false, LakonaGameValueSource.Default, "Lakona:Observability:Logging:File:Enabled"),
        FileLoggingIntegrationRegistered: false,
        TraceExportEnabled: new LakonaGameResolvedValue<bool>(false, LakonaGameValueSource.Default, "Lakona:Observability:Tracing:Export:Enabled"),
        OpenTelemetryIntegrationRegistered: false,
        PrometheusEnabled: new LakonaGameResolvedValue<bool>(false, LakonaGameValueSource.Default, "Lakona:Observability:Metrics:Prometheus:Enabled"),
        PrometheusEndpointRegistered: false,
        PrometheusPath: new LakonaGameResolvedValue<string>("/_lakona/metrics", LakonaGameValueSource.Default, "Lakona:Observability:Metrics:Prometheus:Path"),
        EventBufferCapacity: new LakonaGameResolvedValue<int>(1024, LakonaGameValueSource.Default, "Lakona:Observability:Diagnostics:EventBuffer:Capacity"),
        LoggingMinimumLevel: new LakonaGameResolvedValue<string>("Information", LakonaGameValueSource.Default, "Lakona:Observability:Logging:MinimumLevel"),
        TraceSampleRate: new LakonaGameResolvedValue<double>(1.0, LakonaGameValueSource.Default, "Lakona:Observability:Tracing:Export:SampleRate"));
```

Modify `src/Lakona.Game.Server/Health/LakonaGameReadinessProbe.cs` in Step 6
of this task; that step gives the exact production conversion change.

- [ ] **Step 4: Implement ObservabilityRule**

Create `src/Lakona.Game.Server/Guardrails/Rules/ObservabilityRule.cs`:

```csharp
using System.Net;
using Microsoft.Extensions.Logging;

namespace Lakona.Game.Server.Guardrails.Rules;

public sealed class ObservabilityRule : ILakonaGameValidationRule
{
    public IEnumerable<LakonaGameDiagnostic> Validate(LakonaGameResolvedRuntime runtime)
    {
        var observability = runtime.Observability;
        if (observability.LocalAdminEnabled.Value &&
            observability.LocalAdminRequireLoopback.Value &&
            !IsLoopbackHost(observability.LocalAdminHost.Value))
        {
            yield return Error("ULINK130", "Observability local admin host must bind to loopback.", observability.LocalAdminHost.Path, "Set Lakona:Observability:LocalAdmin:Host to 127.0.0.1 or ::1.");
        }

        if (observability.DetailEnabled.Value)
        {
            yield return new LakonaGameDiagnostic(
                "ULINK131",
                LakonaGameDiagnosticSeverity.Warning,
                Message("Diagnostics detail mode is enabled.", observability.DetailEnabled.Path),
                "Disable Lakona:Observability:Diagnostics:DetailEnabled unless local debugging requires it.");

            if (observability.LocalAdminEnabled.Value && !IsLoopbackHost(observability.LocalAdminHost.Value))
            {
                yield return Error("ULINK132", "Diagnostics detail mode cannot be exposed on non-loopback local admin.", observability.LocalAdminHost.Path, "Bind local admin to loopback or disable detail diagnostics.");
            }
        }

        if (observability.FileLoggingEnabled.Value && !observability.FileLoggingIntegrationRegistered)
        {
            yield return Error("ULINK133", "File logging requires a registered file logging integration.", observability.FileLoggingEnabled.Path, "Install and register the Lakona Serilog observability integration or disable file logging.");
        }

        if (observability.TraceExportEnabled.Value && !observability.OpenTelemetryIntegrationRegistered)
        {
            yield return Error("ULINK134", "Tracing export requires a registered OpenTelemetry integration.", observability.TraceExportEnabled.Path, "Install and register the Lakona OpenTelemetry observability integration or disable tracing export.");
        }

        if (observability.PrometheusEnabled.Value && !observability.PrometheusEndpointRegistered)
        {
            yield return Error("ULINK135", "Prometheus metrics endpoint requires a registered endpoint implementation.", observability.PrometheusEnabled.Path, "Register a Prometheus endpoint implementation or disable Lakona:Observability:Metrics:Prometheus:Enabled.");
        }

        if (!IsValidPath(observability.PrometheusPath.Value))
        {
            yield return Error("ULINK136", "Observability metrics path is invalid.", observability.PrometheusPath.Path, "Use an absolute path such as /_lakona/metrics.");
        }

        if (observability.EventBufferCapacity.Value <= 0)
        {
            yield return Error("ULINK137", "Observability event buffer capacity is invalid.", observability.EventBufferCapacity.Path, "Set Capacity to a positive integer.");
        }

        if (!Enum.TryParse<LogLevel>(observability.LoggingMinimumLevel.Value, ignoreCase: true, out _))
        {
            yield return Error("ULINK138", "Observability log level is invalid.", observability.LoggingMinimumLevel.Path, "Use Trace, Debug, Information, Warning, Error, Critical, or None.");
        }

        if (observability.TraceSampleRate.Value < 0.0 || observability.TraceSampleRate.Value > 1.0)
        {
            yield return Error("ULINK139", "Observability trace sample rate is invalid.", observability.TraceSampleRate.Path, "Set SampleRate between 0.0 and 1.0.");
        }
    }

    private static bool IsLoopbackHost(string host) =>
        host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
        IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address);

    private static bool IsValidPath(string path) =>
        path.StartsWith("/", StringComparison.Ordinal) &&
        !path.Contains('?', StringComparison.Ordinal) &&
        !path.Contains('#', StringComparison.Ordinal) &&
        path.Length > 1;

    private static LakonaGameDiagnostic Error(string code, string message, string? path, string? repair = null) =>
        new(code, LakonaGameDiagnosticSeverity.Error, Message(message, path), repair);

    private static string Message(string message, string? path) =>
        string.IsNullOrWhiteSpace(path) ? message : $"{path}: {message}";
}
```

- [ ] **Step 5: Register the rule**

Modify `src/Lakona.Game.Server/Guardrails/LakonaGameGuardrailServiceCollectionExtensions.cs`:

```csharp
services.TryAddEnumerable(ServiceDescriptor.Singleton<ILakonaGameValidationRule, ObservabilityRule>());
```

- [ ] **Step 6: Update readiness resolved runtime**

Modify `src/Lakona.Game.Server/Health/LakonaGameReadinessProbe.cs` so
`ToResolvedRuntime` includes:

```csharp
private static LakonaGameResolvedObservability ToResolvedObservability(
    LakonaObservabilityOptions options,
    LakonaObservabilityCapabilities capabilities)
{
    return new LakonaGameResolvedObservability(
        LocalAdminEnabled: new LakonaGameResolvedValue<bool>(options.LocalAdmin.EffectiveEnabled, LakonaGameValueSource.Configuration, "Lakona:Observability:LocalAdmin:Enabled"),
        LocalAdminHost: new LakonaGameResolvedValue<string>(options.LocalAdmin.Host, LakonaGameValueSource.Configuration, "Lakona:Observability:LocalAdmin:Host"),
        LocalAdminRequireLoopback: new LakonaGameResolvedValue<bool>(options.LocalAdmin.RequireLoopback, LakonaGameValueSource.Configuration, "Lakona:Observability:LocalAdmin:RequireLoopback"),
        DetailEnabled: new LakonaGameResolvedValue<bool>(options.Diagnostics.DetailEnabled, LakonaGameValueSource.Configuration, "Lakona:Observability:Diagnostics:DetailEnabled"),
        FileLoggingEnabled: new LakonaGameResolvedValue<bool>(options.Logging.File.Enabled, LakonaGameValueSource.Configuration, "Lakona:Observability:Logging:File:Enabled"),
        FileLoggingIntegrationRegistered: capabilities.FileLoggingIntegrationRegistered,
        TraceExportEnabled: new LakonaGameResolvedValue<bool>(options.Tracing.Export.Enabled, LakonaGameValueSource.Configuration, "Lakona:Observability:Tracing:Export:Enabled"),
        OpenTelemetryIntegrationRegistered: capabilities.OpenTelemetryIntegrationRegistered,
        PrometheusEnabled: new LakonaGameResolvedValue<bool>(options.Metrics.Prometheus.Enabled, LakonaGameValueSource.Configuration, "Lakona:Observability:Metrics:Prometheus:Enabled"),
        PrometheusEndpointRegistered: capabilities.PrometheusEndpointRegistered,
        PrometheusPath: new LakonaGameResolvedValue<string>(options.Metrics.Prometheus.Path, LakonaGameValueSource.Configuration, "Lakona:Observability:Metrics:Prometheus:Path"),
        EventBufferCapacity: new LakonaGameResolvedValue<int>(options.Diagnostics.EventBuffer.Capacity, LakonaGameValueSource.Configuration, "Lakona:Observability:Diagnostics:EventBuffer:Capacity"),
        LoggingMinimumLevel: new LakonaGameResolvedValue<string>(options.Logging.MinimumLevel, LakonaGameValueSource.Configuration, "Lakona:Observability:Logging:MinimumLevel"),
        TraceSampleRate: new LakonaGameResolvedValue<double>(options.Tracing.Export.SampleRate, LakonaGameValueSource.Configuration, "Lakona:Observability:Tracing:Export:SampleRate"));
}
```

Add an optional capabilities parameter to readiness:

```csharp
public static int Run(
    LakonaGameRuntimeOptions runtime,
    ClusterOptions? clusterOptions,
    string[] args,
    LakonaObservabilityCapabilities? observabilityCapabilities = null)
```

Update the rule list and resolved-runtime conversion:

```csharp
var capabilities = observabilityCapabilities ?? new LakonaObservabilityCapabilities();
var rules = new List<ILakonaGameValidationRule>
{
    new NodeIdentityRule(),
    new EndpointRule(),
    new HotfixSourceRule(),
    new ObservabilityRule()
};

var resolved = ToResolvedRuntime(runtime, clusterOptions, capabilities);
```

Change the helper signature:

```csharp
internal static LakonaGameResolvedRuntime ToResolvedRuntime(
    LakonaGameRuntimeOptions runtime,
    ClusterOptions? clusterOptions,
    LakonaObservabilityCapabilities capabilities)
```

Replace the end of the `new LakonaGameResolvedRuntime(...)` expression. The
existing hardcoded `Profile: LakonaGameRuntimeProfile.Development` must be
removed:

```csharp
            ReliablePush: new LakonaGameResolvedReliablePush(
                StorageMode: new LakonaGameResolvedValue<string>("InMemory", LakonaGameValueSource.Default),
                PendingLimit: new LakonaGameResolvedValue<int>(256, LakonaGameValueSource.Default),
                ReplayWindowSeconds: new LakonaGameResolvedValue<int>(120, LakonaGameValueSource.Default),
                HasSessionIdentityResolver: true),
            Observability: ToResolvedObservability(runtime.Observability, capabilities),
            Profile: runtime.Profile);
```

- [ ] **Step 7: Add readiness tests for observability diagnostics**

Extend `tests/Lakona.Game.Server.Tests/Health/LakonaGameReadinessProbeTests.cs`:

```csharp
[Fact]
public void Readiness_json_includes_observability_diagnostics()
{
    var runtime = new LakonaGameRuntimeOptions
    {
        Node = new LakonaGameNodeOptions { Id = "battle-1" },
        Profile = LakonaGameRuntimeProfile.Production,
        Observability = LakonaObservabilityOptions.FromConfiguration(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Lakona:Observability:LocalAdmin:Enabled"] = "true",
                    ["Lakona:Observability:LocalAdmin:Host"] = "10.0.0.5"
                })
                .Build(),
            LakonaGameRuntimeProfile.Production)
    };

    var output = CaptureReadiness(runtime, ["--json"], out var exitCode);

    Assert.Equal(1, exitCode);
    Assert.Contains("ULINK130", output, StringComparison.Ordinal);
}

[Fact]
public void Readiness_text_includes_observability_repair()
{
    var runtime = new LakonaGameRuntimeOptions
    {
        Node = new LakonaGameNodeOptions { Id = "battle-1" },
        Profile = LakonaGameRuntimeProfile.Production,
        Observability = LakonaObservabilityOptions.FromConfiguration(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Lakona:Observability:Tracing:Export:Enabled"] = "true"
                })
                .Build(),
            LakonaGameRuntimeProfile.Production)
    };

    var output = CaptureReadiness(runtime, [], out var exitCode);

    Assert.Equal(1, exitCode);
    Assert.Contains("ULINK134", output, StringComparison.Ordinal);
    Assert.Contains("OpenTelemetry", output, StringComparison.Ordinal);
}

[Fact]
public void Readiness_does_not_treat_node_environment_name_as_development()
{
    var runtime = new LakonaGameRuntimeOptions
    {
        Node = new LakonaGameNodeOptions { Id = "battle-1" },
        Profile = LakonaGameRuntimeProfile.Production,
        Observability = LakonaObservabilityOptions.FromConfiguration(
            new ConfigurationBuilder().Build(),
            LakonaGameRuntimeProfile.Production)
    };

    var output = CaptureReadiness(runtime, ["--json"], out var exitCode);

    Assert.Equal(0, exitCode);
    Assert.DoesNotContain("ULINK130", output, StringComparison.Ordinal);
}

[Fact]
public void Readiness_uses_explicit_compose_profile_for_local_admin_defaults()
{
    var runtime = new LakonaGameRuntimeOptions
    {
        Node = new LakonaGameNodeOptions { Id = "dev-1" },
        Profile = LakonaGameRuntimeProfile.Compose,
        Observability = LakonaObservabilityOptions.FromConfiguration(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Lakona:Profile"] = "Compose"
                })
                .Build(),
            LakonaGameRuntimeProfile.Compose)
    };

    var output = CaptureReadiness(runtime, ["--json"], out var exitCode);

    Assert.Equal(0, exitCode);
    Assert.DoesNotContain("ULINK130", output, StringComparison.Ordinal);
}
```

Add this local helper to the readiness test file:

```csharp
private static string CaptureReadiness(
    LakonaGameRuntimeOptions runtime,
    string[] args,
    out int exitCode)
{
    using var output = new StringWriter();
    using var error = new StringWriter();
    var originalOutput = Console.Out;
    var originalError = Console.Error;
    try
    {
        Console.SetOut(output);
        Console.SetError(error);
        exitCode = LakonaGameReadinessProbe.Run(runtime, clusterOptions: null, args);
        return output.ToString() + error.ToString();
    }
    finally
    {
        Console.SetOut(originalOutput);
        Console.SetError(originalError);
    }
}
```

- [ ] **Step 8: Run guardrail and readiness tests**

Run:

```powershell
dotnet test tests\Lakona.Game.Server.Tests\Lakona.Game.Server.Tests.csproj --no-restore --filter "FullyQualifiedName~ObservabilityGuardrailTests|FullyQualifiedName~LakonaGameReadinessProbeTests"
```

Expected: pass.

- [ ] **Step 9: Commit**

```powershell
git add src/Lakona.Game.Server/Guardrails/LakonaGameResolvedObservability.cs src/Lakona.Game.Server/Guardrails/LakonaGameResolvedRuntime.cs src/Lakona.Game.Server/Guardrails/Rules/ObservabilityRule.cs src/Lakona.Game.Server/Guardrails/LakonaGameGuardrailServiceCollectionExtensions.cs src/Lakona.Game.Server/Health/LakonaGameReadinessProbe.cs tests/Lakona.Game.Server.Tests/Observability/ObservabilityGuardrailTests.cs tests/Lakona.Game.Server.Tests/Health/LakonaGameReadinessProbeTests.cs
git commit -m "Add observability guardrails"
```

---

### Task 3: Replace Hotfix Admin Listener with Local Admin Routing

**Files:**
- Create: `src/Lakona.Game.Server/LocalAdmin/ILakonaLocalAdminRoute.cs`
- Create: `src/Lakona.Game.Server/LocalAdmin/LakonaLocalAdminRequest.cs`
- Create: `src/Lakona.Game.Server/LocalAdmin/LakonaLocalAdminResponse.cs`
- Create: `src/Lakona.Game.Server/LocalAdmin/LakonaLocalAdminRouter.cs`
- Create: `src/Lakona.Game.Server/LocalAdmin/LakonaLocalAdminHostedService.cs`
- Create: `src/Lakona.Game.Server/HotfixAdmin/HotfixAdminRoutes.cs`
- Modify: `src/Lakona.Game.Server/HotfixAdmin/HotfixAdminServiceCollectionExtensions.cs`
- Delete: `src/Lakona.Game.Server/HotfixAdmin/HotfixAdminHostedService.cs`
- Test: `tests/Lakona.Game.Server.Tests/LocalAdmin/LakonaLocalAdminRouterTests.cs`

- [ ] **Step 1: Write local admin router tests**

Create `tests/Lakona.Game.Server.Tests/LocalAdmin/LakonaLocalAdminRouterTests.cs`:

```csharp
using System.Text;
using System.Text.Json;
using Lakona.Game.Server.LocalAdmin;
using Xunit;

namespace Lakona.Game.Server.Tests.LocalAdmin;

public sealed class LakonaLocalAdminRouterTests
{
    [Fact]
    public async Task Router_dispatches_matching_method_and_path()
    {
        var router = new LakonaLocalAdminRouter([new EchoRoute()]);

        var response = await router.RouteAsync(new LakonaLocalAdminRequest(
            Method: "GET",
            Path: "/_lakona/test",
            Body: Stream.Null,
            RemoteAddressIsLoopback: true),
            TestContext.Current.CancellationToken);

        Assert.Equal(200, response.StatusCode);
        Assert.Contains("ok", Encoding.UTF8.GetString(response.Body));
    }

    [Fact]
    public async Task Router_rejects_non_loopback_requests()
    {
        var router = new LakonaLocalAdminRouter([new EchoRoute()]);

        var response = await router.RouteAsync(new LakonaLocalAdminRequest(
            Method: "GET",
            Path: "/_lakona/test",
            Body: Stream.Null,
            RemoteAddressIsLoopback: false),
            TestContext.Current.CancellationToken);

        Assert.Equal(403, response.StatusCode);
    }

    [Fact]
    public async Task Router_returns_404_for_unknown_route()
    {
        var router = new LakonaLocalAdminRouter([new EchoRoute()]);

        var response = await router.RouteAsync(new LakonaLocalAdminRequest(
            Method: "GET",
            Path: "/_lakona/missing",
            Body: Stream.Null,
            RemoteAddressIsLoopback: true),
            TestContext.Current.CancellationToken);

        Assert.Equal(404, response.StatusCode);
    }

    private sealed class EchoRoute : ILakonaLocalAdminRoute
    {
        public string Method => "GET";
        public string Path => "/_lakona/test";

        public ValueTask<LakonaLocalAdminResponse> HandleAsync(
            LakonaLocalAdminRequest request,
            CancellationToken cancellationToken)
        {
            return new ValueTask<LakonaLocalAdminResponse>(
                LakonaLocalAdminResponse.Json(new { status = "ok" }));
        }
    }
}
```

- [ ] **Step 2: Run router tests and verify they fail**

Run:

```powershell
dotnet test tests\Lakona.Game.Server.Tests\Lakona.Game.Server.Tests.csproj --no-restore --filter FullyQualifiedName~LakonaLocalAdminRouterTests
```

Expected: compile fails because local admin types do not exist.

- [ ] **Step 3: Add local admin abstractions**

Create `src/Lakona.Game.Server/LocalAdmin/ILakonaLocalAdminRoute.cs`:

```csharp
namespace Lakona.Game.Server.LocalAdmin;

public interface ILakonaLocalAdminRoute
{
    string Method { get; }
    string Path { get; }

    ValueTask<LakonaLocalAdminResponse> HandleAsync(
        LakonaLocalAdminRequest request,
        CancellationToken cancellationToken);
}
```

Create `src/Lakona.Game.Server/LocalAdmin/LakonaLocalAdminRequest.cs`:

```csharp
namespace Lakona.Game.Server.LocalAdmin;

public sealed record LakonaLocalAdminRequest(
    string Method,
    string Path,
    Stream Body,
    bool RemoteAddressIsLoopback);
```

Create `src/Lakona.Game.Server/LocalAdmin/LakonaLocalAdminResponse.cs`:

```csharp
using System.Text;
using System.Text.Json;
using Lakona.Game.Server.HotfixAdmin;

namespace Lakona.Game.Server.LocalAdmin;

public sealed record LakonaLocalAdminResponse(
    int StatusCode,
    string ContentType,
    byte[] Body)
{
    public static LakonaLocalAdminResponse Json(object value, int statusCode = 200)
    {
        var json = JsonSerializer.Serialize(value, HotfixAdminJson.Options);
        return new LakonaLocalAdminResponse(
            statusCode,
            "application/json",
            Encoding.UTF8.GetBytes(json));
    }
}
```

- [ ] **Step 4: Implement router**

Create `src/Lakona.Game.Server/LocalAdmin/LakonaLocalAdminRouter.cs`:

```csharp
namespace Lakona.Game.Server.LocalAdmin;

public sealed class LakonaLocalAdminRouter
{
    private readonly IReadOnlyDictionary<(string Method, string Path), ILakonaLocalAdminRoute> _routes;

    public LakonaLocalAdminRouter(IEnumerable<ILakonaLocalAdminRoute> routes)
    {
        _routes = routes.ToDictionary(
            route => (route.Method.ToUpperInvariant(), route.Path),
            route => route);
    }

    public async ValueTask<LakonaLocalAdminResponse> RouteAsync(
        LakonaLocalAdminRequest request,
        CancellationToken cancellationToken)
    {
        if (!request.RemoteAddressIsLoopback)
        {
            return LakonaLocalAdminResponse.Json(
                new { error = "Lakona local admin accepts loopback requests only." },
                statusCode: 403);
        }

        if (!_routes.TryGetValue((request.Method.ToUpperInvariant(), request.Path), out var route))
        {
            return LakonaLocalAdminResponse.Json(
                new { error = "Unknown Lakona local admin endpoint." },
                statusCode: 404);
        }

        try
        {
            return await route.HandleAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return LakonaLocalAdminResponse.Json(new { error = exception.Message }, statusCode: 400);
        }
    }
}
```

- [ ] **Step 5: Implement hosted service**

Create `src/Lakona.Game.Server/LocalAdmin/LakonaLocalAdminHostedService.cs`:

```csharp
using System.Net;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Lakona.Game.Server.Configuration;

namespace Lakona.Game.Server.LocalAdmin;

public sealed class LakonaLocalAdminHostedService : BackgroundService
{
    private readonly LakonaObservabilityOptions _options;
    private readonly LakonaLocalAdminRouter _router;
    private readonly ILogger<LakonaLocalAdminHostedService> _logger;
    private HttpListener? _listener;

    public LakonaLocalAdminHostedService(
        LakonaObservabilityOptions options,
        LakonaLocalAdminRouter router,
        ILogger<LakonaLocalAdminHostedService> logger)
    {
        _options = options;
        _router = router;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.LocalAdmin.EffectiveEnabled)
        {
            _logger.LogDebug("Lakona local admin endpoint is disabled.");
            return;
        }

        var listener = new HttpListener();
        listener.Prefixes.Add($"http://{_options.LocalAdmin.Host}:{_options.LocalAdmin.Port}/");
        listener.Start();
        _listener = listener;
        _logger.LogInformation(
            "Lakona local admin endpoint listening on {Host}:{Port}.",
            _options.LocalAdmin.Host,
            _options.LocalAdmin.Port);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var context = await listener.GetContextAsync().WaitAsync(stoppingToken).ConfigureAwait(false);
                _ = Task.Run(() => HandleAsync(context, stoppingToken), CancellationToken.None);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            listener.Close();
        }
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _listener?.Close();
        return base.StopAsync(cancellationToken);
    }

    private async Task HandleAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var request = new LakonaLocalAdminRequest(
            context.Request.HttpMethod,
            context.Request.Url?.AbsolutePath ?? "",
            context.Request.InputStream,
            context.Request.RemoteEndPoint?.Address is { } address && IPAddress.IsLoopback(address));

        var response = await _router.RouteAsync(request, cancellationToken).ConfigureAwait(false);
        context.Response.StatusCode = response.StatusCode;
        context.Response.ContentType = response.ContentType;
        context.Response.ContentLength64 = response.Body.Length;
        await context.Response.OutputStream.WriteAsync(response.Body, cancellationToken).ConfigureAwait(false);
        context.Response.Close();
    }
}
```

- [ ] **Step 6: Move hotfix routes into route module**

Create `src/Lakona.Game.Server/HotfixAdmin/HotfixAdminRoutes.cs`:

```csharp
using System.Text.Json;
using Lakona.Game.Server.LocalAdmin;

namespace Lakona.Game.Server.HotfixAdmin;

internal static class HotfixAdminRoutes
{
    public static ILakonaLocalAdminRoute Status(HotfixAdminController controller) =>
        new Route("GET", "/_lakona/hotfix/status",
            async (request, ct) => LakonaLocalAdminResponse.Json(await controller.GetStatusAsync(ct).ConfigureAwait(false)));

    public static ILakonaLocalAdminRoute Activate(HotfixAdminController controller) =>
        new Route("POST", "/_lakona/hotfix/activate",
            async (request, ct) =>
            {
                var body = await JsonSerializer.DeserializeAsync<HotfixActivateRequest>(
                    request.Body,
                    HotfixAdminJson.Options,
                    ct).ConfigureAwait(false) ?? throw new InvalidOperationException("Request body is required.");
                return LakonaLocalAdminResponse.Json(await controller.ActivateAsync(body, ct).ConfigureAwait(false));
            });

    public static ILakonaLocalAdminRoute Rollback(HotfixAdminController controller) =>
        new Route("POST", "/_lakona/hotfix/rollback",
            async (request, ct) => LakonaLocalAdminResponse.Json(await controller.RollbackAsync(ct).ConfigureAwait(false)));

    public static ILakonaLocalAdminRoute Reload(HotfixAdminController controller) =>
        new Route("POST", "/_lakona/hotfix/reload",
            async (request, ct) => LakonaLocalAdminResponse.Json(await controller.ReloadAsync(ct).ConfigureAwait(false)));

    public static IEnumerable<ILakonaLocalAdminRoute> Create(HotfixAdminController controller)
    {
        yield return Status(controller);
        yield return Activate(controller);
        yield return Rollback(controller);
        yield return Reload(controller);
    }

    private sealed class Route : ILakonaLocalAdminRoute
    {
        private readonly Func<LakonaLocalAdminRequest, CancellationToken, ValueTask<LakonaLocalAdminResponse>> _handler;

        public Route(
            string method,
            string path,
            Func<LakonaLocalAdminRequest, CancellationToken, ValueTask<LakonaLocalAdminResponse>> handler)
        {
            Method = method;
            Path = path;
            _handler = handler;
        }

        public string Method { get; }
        public string Path { get; }

        public ValueTask<LakonaLocalAdminResponse> HandleAsync(
            LakonaLocalAdminRequest request,
            CancellationToken cancellationToken) =>
            _handler(request, cancellationToken);
    }
}
```

- [ ] **Step 7: Update hotfix admin service registration**

Modify `src/Lakona.Game.Server/HotfixAdmin/HotfixAdminServiceCollectionExtensions.cs`:

```csharp
services.AddSingleton(options);
services.AddSingleton(sp => new HotfixVersionStore(options.HotfixRoot));
services.AddSingleton<HotfixAdminController>();
services.AddSingleton<ILakonaLocalAdminRoute>(sp =>
    HotfixAdminRoutes.Status(sp.GetRequiredService<HotfixAdminController>()));
services.AddSingleton<ILakonaLocalAdminRoute>(sp =>
    HotfixAdminRoutes.Activate(sp.GetRequiredService<HotfixAdminController>()));
services.AddSingleton<ILakonaLocalAdminRoute>(sp =>
    HotfixAdminRoutes.Rollback(sp.GetRequiredService<HotfixAdminController>()));
services.AddSingleton<ILakonaLocalAdminRoute>(sp =>
    HotfixAdminRoutes.Reload(sp.GetRequiredService<HotfixAdminController>()));
```

Remove hosted service registration for `HotfixAdminHostedService`; only
`LakonaLocalAdminHostedService` owns the listener.

- [ ] **Step 8: Delete old hosted service**

Delete `src/Lakona.Game.Server/HotfixAdmin/HotfixAdminHostedService.cs`.

- [ ] **Step 9: Register local admin host in core services**

Create `src/Lakona.Game.Server/Observability/LakonaObservabilityServiceCollectionExtensions.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.LocalAdmin;

namespace Lakona.Game.Server.Observability;

public static class LakonaObservabilityServiceCollectionExtensions
{
    public static IServiceCollection AddLakonaGameObservability(
        this IServiceCollection services)
    {
        services.TryAddSingleton(sp =>
            sp.GetRequiredService<LakonaGameRuntimeOptions>().Observability);
        services.TryAddSingleton(sp =>
            LakonaObservabilityCapabilities.FromServices(
                sp.GetServices<ILakonaObservabilityCapability>()));
        services.TryAddSingleton<LakonaLocalAdminRouter>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, LakonaLocalAdminHostedService>());
        return services;
    }

    public static IServiceCollection AddLakonaGameObservability(
        this IServiceCollection services,
        LakonaObservabilityOptions options)
    {
        services.TryAddSingleton(options);
        return services.AddLakonaGameObservability();
    }
}
```

Modify `src/Lakona.Game.Server/LakonaGameServerServiceCollectionExtensions.cs` to call:

```csharp
services.AddLakonaGameObservability();
```

- [ ] **Step 10: Run local admin and existing hotfix admin tests**

Run:

```powershell
dotnet test tests\Lakona.Game.Server.Tests\Lakona.Game.Server.Tests.csproj --no-restore --filter "FullyQualifiedName~LakonaLocalAdminRouterTests|FullyQualifiedName~HotfixAdminTests"
```

Expected: pass.

- [ ] **Step 11: Commit**

```powershell
git add src/Lakona.Game.Server/LocalAdmin src/Lakona.Game.Server/HotfixAdmin src/Lakona.Game.Server/Observability/LakonaObservabilityServiceCollectionExtensions.cs src/Lakona.Game.Server/LakonaGameServerServiceCollectionExtensions.cs tests/Lakona.Game.Server.Tests/LocalAdmin/LakonaLocalAdminRouterTests.cs
git commit -m "Unify local admin routing"
```

---

### Task 4: Decouple Production Hotfix Mode from Local Admin Enablement

**Files:**
- Modify: `src/Lakona.Game.Server/Hosting/LakonaGameServer.cs`
- Modify: `src/Lakona.Game.Server/HotfixAdmin/HotfixAdminOptions.cs`
- Test: `tests/Lakona.Game.Server.Tests/HotfixAdminTests.cs`

- [ ] **Step 1: Write source selection tests**

Add to `tests/Lakona.Game.Server.Tests/HotfixAdminTests.cs`:

```csharp
[Fact]
public void Production_hotfix_mode_uses_version_pointer_source_even_when_local_admin_is_disabled()
{
    using var fixture = HotfixAdminFixture.Create();

    var source = LakonaGameServer.CreateDefaultHotfixAssemblySourceForTesting(
        fixture.Root,
        new HotfixAdminOptions
        {
            Enabled = false,
            Mode = "production",
            HotfixRoot = fixture.Root,
            BuildTag = HotfixBuildTag.Get(typeof(HotfixAdminTests).Assembly)
        });

    Assert.IsType<VersionPointerHotfixAssemblySource>(source);
}

[Fact]
public void Development_hotfix_mode_uses_current_directory_source()
{
    using var fixture = HotfixAdminFixture.Create();

    var source = LakonaGameServer.CreateDefaultHotfixAssemblySourceForTesting(
        fixture.Root,
        new HotfixAdminOptions
        {
            Enabled = false,
            Mode = "development",
            HotfixRoot = fixture.Root,
            BuildTag = HotfixBuildTag.Get(typeof(HotfixAdminTests).Assembly)
        });

    Assert.IsType<CurrentDirectoryHotfixAssemblySource>(source);
}
```

- [ ] **Step 2: Run tests and verify current bug**

Run:

```powershell
dotnet test tests\Lakona.Game.Server.Tests\Lakona.Game.Server.Tests.csproj --no-restore --filter "FullyQualifiedName~Production_hotfix_mode_uses_version_pointer_source_even_when_local_admin_is_disabled|FullyQualifiedName~Development_hotfix_mode_uses_current_directory_source"
```

Expected: first test fails under current source selection because source type is
coupled to `Enabled`.

- [ ] **Step 3: Extract source factory and decouple Enabled**

Modify `src/Lakona.Game.Server/Hosting/LakonaGameServer.cs`:

```csharp
internal static IHotfixAssemblySource CreateDefaultHotfixAssemblySourceForTesting(
    string baseDirectory,
    HotfixAdminOptions adminOptions)
{
    return CreateDefaultHotfixAssemblySource(baseDirectory, adminOptions);
}

private static IHotfixAssemblySource CreateDefaultHotfixAssemblySource(
    string baseDirectory,
    HotfixAdminOptions adminOptions)
{
    var hotfixDirectory = Path.Combine(baseDirectory, "hotfix");
    return adminOptions.Mode.Equals("production", StringComparison.OrdinalIgnoreCase)
        ? new VersionPointerHotfixAssemblySource(hotfixDirectory, "current.txt", "Server.Hotfix.dll")
        : new CurrentDirectoryHotfixAssemblySource(hotfixDirectory, "Server.Hotfix.dll");
}

private static void ConfigureDefaultHotfix(
    IServiceCollection services,
    string baseDirectory,
    HotfixAdminOptions adminOptions)
{
    services.AddLakonaGameHotfix(
        CreateDefaultHotfixAssemblySource(baseDirectory, adminOptions),
        sharedAssemblyNames: GetDefaultHotfixSharedAssemblyNames());
    services.AddLakonaGameHotfixActorTicks();
}
```

- [ ] **Step 4: Clarify HotfixAdminOptions responsibility**

Keep `Enabled`, `Host`, and `Port` temporarily if existing code/tests still bind
legacy `Lakona:Hotfix:Admin`, but add comments and do not use them for source
selection:

```csharp
// Listener settings are owned by Lakona:Observability:LocalAdmin.
// These properties are kept only for legacy configuration binding during the
// local admin migration.
```

- [ ] **Step 5: Run source selection tests**

Run:

```powershell
dotnet test tests\Lakona.Game.Server.Tests\Lakona.Game.Server.Tests.csproj --no-restore --filter "FullyQualifiedName~Production_hotfix_mode_uses_version_pointer_source_even_when_local_admin_is_disabled|FullyQualifiedName~Development_hotfix_mode_uses_current_directory_source"
```

Expected: pass.

- [ ] **Step 6: Commit**

```powershell
git add src/Lakona.Game.Server/Hosting/LakonaGameServer.cs src/Lakona.Game.Server/HotfixAdmin/HotfixAdminOptions.cs tests/Lakona.Game.Server.Tests/HotfixAdminTests.cs
git commit -m "Decouple hotfix source mode from admin listener"
```

---

### Task 5: Add Diagnostics Event Buffer

**Files:**
- Create: `src/Lakona.Game.Server/Observability/Diagnostics/DiagnosticsEvent.cs`
- Create: `src/Lakona.Game.Server/Observability/Diagnostics/IDiagnosticsEventSink.cs`
- Create: `src/Lakona.Game.Server/Observability/Diagnostics/BoundedDiagnosticsEventBuffer.cs`
- Create: `src/Lakona.Game.Server/Observability/Diagnostics/DiagnosticsEventLoggerProvider.cs`
- Create: `src/Lakona.Game.Server/Observability/Diagnostics/ActorDiagnosticsEventBridge.cs`
- Create: `src/Lakona.Game.Server/Actors/IActorDiagnosticsObserver.cs`
- Modify: `src/Lakona.Game.Server/Actors/LakonaActorRuntime.cs`
- Modify: `src/Lakona.Game.Server/Observability/LakonaObservabilityServiceCollectionExtensions.cs`
- Test: `tests/Lakona.Game.Server.Tests/Observability/DiagnosticsEventBufferTests.cs`

- [ ] **Step 1: Write event buffer tests**

Create `tests/Lakona.Game.Server.Tests/Observability/DiagnosticsEventBufferTests.cs`:

```csharp
using System.Text.Json;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Observability.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using GameActor = Lakona.Game.Server.Actors.Actor;

namespace Lakona.Game.Server.Tests.Observability;

public sealed class DiagnosticsEventBufferTests
{
    [Fact]
    public void Buffer_keeps_most_recent_events_within_capacity()
    {
        var buffer = new BoundedDiagnosticsEventBuffer(capacity: 2, minimumLevel: LogLevel.Warning);

        buffer.Publish(Event("one", LogLevel.Warning));
        buffer.Publish(Event("two", LogLevel.Error));
        buffer.Publish(Event("three", LogLevel.Critical));

        var events = buffer.Snapshot(limit: 10);

        Assert.Equal(["three", "two"], events.Select(e => e.Message).ToArray());
    }

    [Fact]
    public void Buffer_filters_events_below_minimum_level()
    {
        var buffer = new BoundedDiagnosticsEventBuffer(capacity: 10, minimumLevel: LogLevel.Warning);

        buffer.Publish(Event("debug", LogLevel.Debug));
        buffer.Publish(Event("warning", LogLevel.Warning));

        var item = Assert.Single(buffer.Snapshot(limit: 10));
        Assert.Equal("warning", item.Message);
    }

    [Fact]
    public void Buffer_limits_snapshot_count()
    {
        var buffer = new BoundedDiagnosticsEventBuffer(capacity: 10, minimumLevel: LogLevel.Information);

        buffer.Publish(Event("one", LogLevel.Information));
        buffer.Publish(Event("two", LogLevel.Information));
        buffer.Publish(Event("three", LogLevel.Information));

        Assert.Equal(["three", "two"], buffer.Snapshot(limit: 2).Select(e => e.Message));
    }

    [Fact]
    public void Sanitized_event_does_not_accept_sensitive_identifiers()
    {
        var evt = Event("safe", LogLevel.Warning, dimensions: new Dictionary<string, string>
        {
            ["actor_type"] = "RoomActor",
            ["message_type"] = "JoinRoom"
        });

        Assert.DoesNotContain(evt.Dimensions.Keys, key =>
            key.Contains("actor_id", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("session_id", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("token", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("payload", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("call_chain", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Logger_provider_captures_lakona_warning_without_rendering_sensitive_values()
    {
        var buffer = new BoundedDiagnosticsEventBuffer(capacity: 10, minimumLevel: LogLevel.Warning);
        using var provider = new DiagnosticsEventLoggerProvider(buffer, LogLevel.Warning);
        var logger = provider.CreateLogger("Lakona.Game.Session");

        logger.LogWarning("Session failed for token {Token}", "secret-token");

        var json = JsonSerializer.Serialize(buffer.Snapshot(10));
        Assert.Contains("framework.log", json, StringComparison.Ordinal);
        Assert.Contains("Lakona.Game.Session", json, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-token", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Session failed for token", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Actor_bridge_publishes_sanitized_events_without_actor_ids_or_payloads()
    {
        var buffer = new BoundedDiagnosticsEventBuffer(capacity: 10, minimumLevel: LogLevel.Warning);
        var bridge = new ActorDiagnosticsEventBridge(buffer);

        bridge.OnDeadLetter(new ActorDeadLetterDiagnosticsEvent(
            MessageType: typeof(SecretMessage).FullName!,
            Reason: "Actor does not exist."));
        bridge.OnSlowMessage(new ActorSlowMessageDiagnosticsEvent(
            MessageType: typeof(SecretMessage).FullName!,
            Elapsed: TimeSpan.FromMilliseconds(42)));
        bridge.OnCallTimeout(new ActorCallTimeoutDiagnosticsEvent(
            RequestType: typeof(SecretMessage).FullName!,
            Timeout: TimeSpan.FromSeconds(1),
            Reason: ActorCallTimeoutReason.ResponseTimeout));

        var json = JsonSerializer.Serialize(buffer.Snapshot(10));

        Assert.Contains("actor.dead_letter", json, StringComparison.Ordinal);
        Assert.Contains("actor.slow_message", json, StringComparison.Ordinal);
        Assert.Contains("actor.call_timeout", json, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-actor", json, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-caller", json, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-payload", json, StringComparison.Ordinal);
        Assert.DoesNotContain("call_chain", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Actor_observer_publishes_event_when_user_callback_throws()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var buffer = new BoundedDiagnosticsEventBuffer(capacity: 10, minimumLevel: LogLevel.Warning);
        await using var provider = new ServiceCollection()
            .AddSingleton<IDiagnosticsEventSink>(buffer)
            .AddSingleton<IActorDiagnosticsObserver, ActorDiagnosticsEventBridge>()
            .AddLakonaGameServerActors(options =>
            {
                options.SlowMessageThreshold = TimeSpan.FromMilliseconds(1);
                options.SlowMessageHandler = _ => throw new InvalidOperationException("user callback secret-payload");
            })
            .BuildServiceProvider();
        var lifecycle = provider.GetRequiredService<IActorLifecycle>();
        var runtime = provider.GetRequiredService<IActorRuntime>();
        var id = ActorId.From("secret-actor");

        await lifecycle.CreateLocalAsync<BridgeActor>(id, cancellationToken: cancellationToken);
        await runtime.TellAsync<BridgeActor>(
            id,
            static (actor, ct) => actor.DelayAsync(TimeSpan.FromMilliseconds(50), ct),
            cancellationToken);

        await WaitForEventAsync(buffer, "actor.slow_message", cancellationToken);
        var json = JsonSerializer.Serialize(buffer.Snapshot(10));
        Assert.Contains("actor.slow_message", json, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-actor", json, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-payload", json, StringComparison.Ordinal);
    }

    private static DiagnosticsEvent Event(
        string message,
        LogLevel level,
        IReadOnlyDictionary<string, string>? dimensions = null) =>
        new(
            TimestampUtc: DateTimeOffset.UtcNow,
            Level: level,
            Category: "Lakona.Game.Actor",
            Kind: "test",
            Message: message,
            TraceId: null,
            CorrelationId: null,
            Dimensions: dimensions ?? new Dictionary<string, string>());

    private static async Task WaitForEventAsync(
        BoundedDiagnosticsEventBuffer buffer,
        string kind,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(2);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (buffer.Snapshot(10).Any(item => item.Kind == kind))
            {
                return;
            }

            await Task.Delay(10, cancellationToken);
        }

        Assert.Contains(buffer.Snapshot(10), item => item.Kind == kind);
    }

    private sealed record SecretMessage(string Value);

    private sealed class BridgeActor : GameActor
    {
        public async ValueTask DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            await Task.Delay(delay, cancellationToken);
        }
    }
}
```

- [ ] **Step 2: Run event buffer tests and verify they fail**

Run:

```powershell
dotnet test tests\Lakona.Game.Server.Tests\Lakona.Game.Server.Tests.csproj --no-restore --filter FullyQualifiedName~DiagnosticsEventBufferTests
```

Expected: compile fails because event buffer types do not exist.

- [ ] **Step 3: Implement event DTO and sink**

Create `src/Lakona.Game.Server/Observability/Diagnostics/DiagnosticsEvent.cs`:

```csharp
using Microsoft.Extensions.Logging;

namespace Lakona.Game.Server.Observability.Diagnostics;

public sealed record DiagnosticsEvent(
    DateTimeOffset TimestampUtc,
    LogLevel Level,
    string Category,
    string Kind,
    string Message,
    string? TraceId,
    string? CorrelationId,
    IReadOnlyDictionary<string, string> Dimensions);
```

Create `src/Lakona.Game.Server/Observability/Diagnostics/IDiagnosticsEventSink.cs`:

```csharp
namespace Lakona.Game.Server.Observability.Diagnostics;

public interface IDiagnosticsEventSink
{
    void Publish(DiagnosticsEvent diagnosticEvent);

    IReadOnlyList<DiagnosticsEvent> Snapshot(int limit);
}
```

- [ ] **Step 4: Implement bounded buffer**

Create `src/Lakona.Game.Server/Observability/Diagnostics/BoundedDiagnosticsEventBuffer.cs`:

```csharp
using Microsoft.Extensions.Logging;

namespace Lakona.Game.Server.Observability.Diagnostics;

public sealed class BoundedDiagnosticsEventBuffer : IDiagnosticsEventSink
{
    private readonly object _gate = new();
    private readonly Queue<DiagnosticsEvent> _events = new();
    private readonly int _capacity;
    private readonly LogLevel _minimumLevel;

    public BoundedDiagnosticsEventBuffer(int capacity, LogLevel minimumLevel)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be positive.");
        }

        _capacity = capacity;
        _minimumLevel = minimumLevel;
    }

    public void Publish(DiagnosticsEvent diagnosticEvent)
    {
        if (diagnosticEvent.Level < _minimumLevel)
        {
            return;
        }

        lock (_gate)
        {
            _events.Enqueue(diagnosticEvent);
            while (_events.Count > _capacity)
            {
                _events.Dequeue();
            }
        }
    }

    public IReadOnlyList<DiagnosticsEvent> Snapshot(int limit)
    {
        if (limit <= 0)
        {
            return Array.Empty<DiagnosticsEvent>();
        }

        lock (_gate)
        {
            return _events
                .Reverse()
                .Take(limit)
                .ToArray();
        }
    }
}
```

- [ ] **Step 5: Add sanitized logger provider bridge**

Create `src/Lakona.Game.Server/Observability/Diagnostics/DiagnosticsEventLoggerProvider.cs`:

```csharp
using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging;

namespace Lakona.Game.Server.Observability.Diagnostics;

public sealed class DiagnosticsEventLoggerProvider : ILoggerProvider
{
    private readonly IDiagnosticsEventSink _events;
    private readonly LogLevel _minimumLevel;

    public DiagnosticsEventLoggerProvider(IDiagnosticsEventSink events)
        : this(events, LogLevel.Warning)
    {
    }

    public DiagnosticsEventLoggerProvider(IDiagnosticsEventSink events, LogLevel minimumLevel)
    {
        _events = events;
        _minimumLevel = minimumLevel;
    }

    public ILogger CreateLogger(string categoryName) =>
        new Logger(categoryName, _events, _minimumLevel);

    public void Dispose()
    {
    }

    private sealed class Logger : ILogger
    {
        private readonly string _categoryName;
        private readonly IDiagnosticsEventSink _events;
        private readonly LogLevel _minimumLevel;

        public Logger(string categoryName, IDiagnosticsEventSink events, LogLevel minimumLevel)
        {
            _categoryName = categoryName;
            _events = events;
            _minimumLevel = minimumLevel;
        }

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull =>
            NoopScope.Instance;

        public bool IsEnabled(LogLevel logLevel) =>
            logLevel >= _minimumLevel &&
            _categoryName.StartsWith("Lakona.", StringComparison.Ordinal);

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var dimensions = new Dictionary<string, string>
            {
                ["event_id"] = eventId.Id.ToString(CultureInfo.InvariantCulture)
            };

            if (exception is not null)
            {
                dimensions["exception_type"] = exception.GetType().FullName ?? exception.GetType().Name;
            }

            _events.Publish(new DiagnosticsEvent(
                TimestampUtc: DateTimeOffset.UtcNow,
                Level: logLevel,
                Category: _categoryName,
                Kind: "framework.log",
                Message: $"{logLevel} framework log.",
                TraceId: Activity.Current?.TraceId.ToString(),
                CorrelationId: null,
                Dimensions: dimensions));
        }
    }

    private sealed class NoopScope : IDisposable
    {
        public static readonly NoopScope Instance = new();

        public void Dispose()
        {
        }
    }
}
```
Do not call `formatter` and do not inspect `state`; either can render user
payloads or structured values into the event buffer.

- [ ] **Step 6: Add actor diagnostics observer bridge**

Create `src/Lakona.Game.Server/Actors/IActorDiagnosticsObserver.cs`:

```csharp
namespace Lakona.Game.Server.Actors;

public sealed record ActorDeadLetterDiagnosticsEvent(
    string MessageType,
    string Reason);

public sealed record ActorSlowMessageDiagnosticsEvent(
    string MessageType,
    TimeSpan Elapsed);

public sealed record ActorCallTimeoutDiagnosticsEvent(
    string RequestType,
    TimeSpan Timeout,
    ActorCallTimeoutReason Reason);

public interface IActorDiagnosticsObserver
{
    void OnDeadLetter(ActorDeadLetterDiagnosticsEvent diagnostic);

    void OnSlowMessage(ActorSlowMessageDiagnosticsEvent diagnostic);

    void OnCallTimeout(ActorCallTimeoutDiagnosticsEvent diagnostic);
}
```

Modify `src/Lakona.Game.Server/Actors/LakonaActorRuntime.cs`:

```csharp
private readonly IReadOnlyList<IActorDiagnosticsObserver> _diagnosticsObservers;

public LakonaActorRuntime(
    IServiceProvider services,
    ActorRuntimeOptions options,
    IEnumerable<IActorDiagnosticsObserver>? diagnosticsObservers = null)
{
    _services = services ?? throw new ArgumentNullException(nameof(services));
    _options = options ?? throw new ArgumentNullException(nameof(options));
    _diagnosticsObservers = diagnosticsObservers?.ToArray() ?? Array.Empty<IActorDiagnosticsObserver>();
    _actorSystem = new K.ActorSystem(new K.ActorSystemOptions
    {
        MailboxCapacity = Math.Max(1, options.MailboxCapacity),
        SlowMessageThreshold = options.SlowMessageThreshold,
        MessageInterceptor = options.MessageInterceptor is null
            ? null
            : new KernelMessageInterceptorAdapter(this, options.MessageInterceptor)
    });
    _actorSystem.DeadLetterPublished += OnDeadLetterPublished;
    _actorSystem.SlowMessageDetected += OnSlowMessageDetected;
    _actorSystem.CallTimedOut += OnCallTimedOut;
}
```

Replace the three public diagnostic callback methods with local variables and
observer notification:

```csharp
private void OnDeadLetterPublished(K.DeadLetter deadLetter)
{
    PublishToObservers(observer => observer.OnDeadLetter(new ActorDeadLetterDiagnosticsEvent(
        MessageType: deadLetter.MessageType,
        Reason: deadLetter.Reason)));

    var diagnostic = new ActorDeadLetterDiagnostic(
        MapActorId(deadLetter.Target),
        deadLetter.MessageType,
        deadLetter.Reason);
    _options.DeadLetterHandler?.Invoke(diagnostic);
}

private void OnSlowMessageDetected(K.SlowMessage slowMessage)
{
    PublishToObservers(observer => observer.OnSlowMessage(new ActorSlowMessageDiagnosticsEvent(
        MessageType: slowMessage.MessageType,
        Elapsed: slowMessage.Elapsed)));

    var diagnostic = new ActorSlowMessageDiagnostic(
        MapActorId(slowMessage.ActorId),
        slowMessage.MessageType,
        slowMessage.Elapsed);
    _options.SlowMessageHandler?.Invoke(diagnostic);
}

private void OnCallTimedOut(K.ActorCallTimeout timeout)
{
    var publicReason = MapCallTimeoutReason(timeout.Reason);
    PublishToObservers(observer => observer.OnCallTimeout(new ActorCallTimeoutDiagnosticsEvent(
        RequestType: timeout.RequestType,
        Timeout: MapCallTimeout(timeout),
        Reason: publicReason)));

    var diagnostic = new ActorCallTimeoutDiagnostic(
        timeout.Caller is { } caller ? MapActorId(caller) : null,
        MapActorId(timeout.Target),
        timeout.RequestType,
        MapCallTimeout(timeout),
        publicReason,
        timeout.CallChain.Select(MapActorId).ToArray());
    _options.CallTimeoutHandler?.Invoke(diagnostic);
}

private void PublishToObservers(Action<IActorDiagnosticsObserver> publish)
{
    foreach (var observer in _diagnosticsObservers)
    {
        try
        {
            publish(observer);
        }
        catch
        {
            // Diagnostics bridges must not break actor dispatch or user callbacks.
        }
    }
}
```

Create `src/Lakona.Game.Server/Observability/Diagnostics/ActorDiagnosticsEventBridge.cs`:

```csharp
using System.Diagnostics;
using System.Globalization;
using Lakona.Game.Server.Actors;
using Microsoft.Extensions.Logging;

namespace Lakona.Game.Server.Observability.Diagnostics;

public sealed class ActorDiagnosticsEventBridge : IActorDiagnosticsObserver
{
    private readonly IDiagnosticsEventSink _events;

    public ActorDiagnosticsEventBridge(IDiagnosticsEventSink events)
    {
        _events = events;
    }

    public void OnDeadLetter(ActorDeadLetterDiagnosticsEvent diagnostic)
    {
        _events.Publish(Create(
            LogLevel.Warning,
            "actor.dead_letter",
            "Actor message was rejected.",
            new Dictionary<string, string>
            {
                ["message_type"] = Bound(diagnostic.MessageType),
                ["reason"] = ClassifyDeadLetterReason(diagnostic.Reason)
            }));
    }

    public void OnSlowMessage(ActorSlowMessageDiagnosticsEvent diagnostic)
    {
        _events.Publish(Create(
            LogLevel.Warning,
            "actor.slow_message",
            "Actor message exceeded the slow-message threshold.",
            new Dictionary<string, string>
            {
                ["message_type"] = Bound(diagnostic.MessageType),
                ["elapsed_ms"] = ((long)diagnostic.Elapsed.TotalMilliseconds).ToString(CultureInfo.InvariantCulture)
            }));
    }

    public void OnCallTimeout(ActorCallTimeoutDiagnosticsEvent diagnostic)
    {
        _events.Publish(Create(
            LogLevel.Error,
            "actor.call_timeout",
            "Actor call timed out.",
            new Dictionary<string, string>
            {
                ["request_type"] = Bound(diagnostic.RequestType),
                ["reason"] = diagnostic.Reason.ToString(),
                ["timeout_ms"] = ((long)diagnostic.Timeout.TotalMilliseconds).ToString(CultureInfo.InvariantCulture)
            }));
    }

    private static DiagnosticsEvent Create(
        LogLevel level,
        string kind,
        string message,
        IReadOnlyDictionary<string, string> dimensions) =>
        new(
            TimestampUtc: DateTimeOffset.UtcNow,
            Level: level,
            Category: "Lakona.Game.Actor",
            Kind: kind,
            Message: message,
            TraceId: Activity.Current?.TraceId.ToString(),
            CorrelationId: null,
            Dimensions: dimensions);

    private static string ClassifyDeadLetterReason(string reason)
    {
        if (reason.Contains("does not exist", StringComparison.OrdinalIgnoreCase))
        {
            return "actor_not_found";
        }

        if (reason.Contains("stopping", StringComparison.OrdinalIgnoreCase))
        {
            return "actor_stopping";
        }

        if (reason.Contains("completed", StringComparison.OrdinalIgnoreCase))
        {
            return "mailbox_completed";
        }

        return "other";
    }

    private static string Bound(string value) =>
        value.Length <= 128 ? value : value[..128];
}
```

- [ ] **Step 7: Register event buffer, logger provider, and actor bridge**

Modify `src/Lakona.Game.Server/Observability/LakonaObservabilityServiceCollectionExtensions.cs`.
Ensure the file has:

```csharp
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Observability.Diagnostics;
using Microsoft.Extensions.Logging;
```

Add these registrations inside `AddLakonaGameObservability` after the local
admin router/host registrations:

```csharp
services.TryAddSingleton<IDiagnosticsEventSink>(sp =>
{
    var options = sp.GetRequiredService<LakonaObservabilityOptions>();
    var level = Enum.TryParse<LogLevel>(
        options.Diagnostics.EventBuffer.MinimumLevel,
        ignoreCase: true,
        out var parsed)
        ? parsed
        : LogLevel.Warning;
    return new BoundedDiagnosticsEventBuffer(
        options.Diagnostics.EventBuffer.Capacity,
        level);
});
services.TryAddEnumerable(ServiceDescriptor.Singleton<ILoggerProvider, DiagnosticsEventLoggerProvider>());
services.TryAddEnumerable(ServiceDescriptor.Singleton<IActorDiagnosticsObserver, ActorDiagnosticsEventBridge>());
```

- [ ] **Step 8: Run event buffer tests**

Run:

```powershell
dotnet test tests\Lakona.Game.Server.Tests\Lakona.Game.Server.Tests.csproj --no-restore --filter FullyQualifiedName~DiagnosticsEventBufferTests
```

Expected: pass.

- [ ] **Step 9: Commit**

```powershell
git add src/Lakona.Game.Server/Observability/Diagnostics/DiagnosticsEvent.cs src/Lakona.Game.Server/Observability/Diagnostics/IDiagnosticsEventSink.cs src/Lakona.Game.Server/Observability/Diagnostics/BoundedDiagnosticsEventBuffer.cs src/Lakona.Game.Server/Observability/Diagnostics/DiagnosticsEventLoggerProvider.cs src/Lakona.Game.Server/Observability/Diagnostics/ActorDiagnosticsEventBridge.cs src/Lakona.Game.Server/Actors/IActorDiagnosticsObserver.cs src/Lakona.Game.Server/Actors/LakonaActorRuntime.cs src/Lakona.Game.Server/Observability/LakonaObservabilityServiceCollectionExtensions.cs tests/Lakona.Game.Server.Tests/Observability/DiagnosticsEventBufferTests.cs
git commit -m "Add diagnostics event buffer"
```

---

### Task 6: Add Safe Runtime Snapshot APIs

**Files:**
- Modify: `src/Lakona.Game.Server/Actors/IActorRuntime.cs`
- Modify: `src/Lakona.Game.Server/Actors/LakonaActorRuntime.cs`
- Create: `src/Lakona.Game.Server/Actors/ActorRuntimeDiagnosticsSnapshot.cs`
- Modify: `src/Lakona.Game.Server/Sessions/IGameSessionRegistry.cs`
- Modify: `src/Lakona.Game.Server/Sessions/InMemoryGameSessionRegistry.cs`
- Create: `src/Lakona.Game.Server/Sessions/GameSessionDiagnosticsSnapshot.cs`
- Test: `tests/Lakona.Game.Server.Tests/ActorRuntimeTests.cs`
- Test: `tests/Lakona.Game.Server.Tests/GameSessionRegistryTests.cs`

- [ ] **Step 1: Add actor diagnostics snapshot tests**

Add to `tests/Lakona.Game.Server.Tests/ActorRuntimeTests.cs`:

```csharp
[Fact]
public async Task Actor_diagnostics_snapshot_aggregates_by_actor_type_without_actor_ids()
{
    var cancellationToken = TestContext.Current.CancellationToken;
    await using var provider = CreateProvider();
    var lifecycle = provider.GetRequiredService<IActorLifecycle>();
    var runtime = provider.GetRequiredService<IActorRuntime>();

    await lifecycle.CreateLocalAsync<TestActor>(ActorId.From("secret-actor-id"), cancellationToken: cancellationToken);

    var snapshot = runtime.GetDiagnosticsSnapshot();

    var item = Assert.Single(snapshot.ActorTypes, type => type.ActorType == typeof(TestActor).FullName);
    Assert.Equal(1, item.ActiveCount);
    Assert.DoesNotContain("secret-actor-id", snapshot.ToString(), StringComparison.Ordinal);
}
```

- [ ] **Step 2: Add session diagnostics snapshot tests**

Add to `tests/Lakona.Game.Server.Tests/GameSessionRegistryTests.cs`:

```csharp
[Fact]
public async Task Session_diagnostics_snapshot_counts_sessions_without_ids_or_tokens()
{
    var registry = new InMemoryGameSessionRegistry();
    var session = await registry.StartNewSessionAsync("player-secret", TestContext.Current.CancellationToken);
    await registry.BindSessionAsync(session, "connection-secret", new TestCallback(), TestContext.Current.CancellationToken);

    var snapshot = registry.GetDiagnosticsSnapshot();

    Assert.Equal(1, snapshot.ActiveSessions);
    Assert.Equal(1, snapshot.ActiveConnections);
    Assert.DoesNotContain("player-secret", snapshot.ToString(), StringComparison.Ordinal);
    Assert.DoesNotContain("connection-secret", snapshot.ToString(), StringComparison.Ordinal);
    Assert.DoesNotContain(session.SessionId, snapshot.ToString(), StringComparison.Ordinal);
}

private sealed class TestCallback;
```

- [ ] **Step 3: Run snapshot tests and verify they fail**

Run:

```powershell
dotnet test tests\Lakona.Game.Server.Tests\Lakona.Game.Server.Tests.csproj --no-restore --filter "FullyQualifiedName~Actor_diagnostics_snapshot|FullyQualifiedName~Session_diagnostics_snapshot"
```

Expected: compile fails because snapshot APIs do not exist.

- [ ] **Step 4: Add actor snapshot DTO and API**

Create `src/Lakona.Game.Server/Actors/ActorRuntimeDiagnosticsSnapshot.cs`:

```csharp
namespace Lakona.Game.Server.Actors;

public sealed record ActorRuntimeDiagnosticsSnapshot(
    IReadOnlyList<ActorTypeDiagnosticsSnapshot> ActorTypes);

public sealed record ActorTypeDiagnosticsSnapshot(
    string ActorType,
    int ActiveCount,
    int MailboxQueuedSum,
    int MailboxQueuedMax,
    long MailboxEnqueuedCount,
    long MailboxProcessedCount,
    long MailboxRejectedCount);
```

Modify `src/Lakona.Game.Server/Actors/IActorRuntime.cs`:

```csharp
ActorRuntimeDiagnosticsSnapshot GetDiagnosticsSnapshot();
```

Add this method to `src/Lakona.Game.Server/Actors/LakonaActorRuntime.cs`:

```csharp
public ActorRuntimeDiagnosticsSnapshot GetDiagnosticsSnapshot()
{
    var actorTypes = _actors.Values
        .Where(cell => cell.GetState() == ActorState.Active)
        .GroupBy(
            cell => cell.ActorType.FullName ?? cell.ActorType.Name,
            StringComparer.Ordinal)
        .Select(group =>
        {
            var metrics = group
                .Select(cell => cell.GetMailboxMetrics())
                .ToArray();
            return new ActorTypeDiagnosticsSnapshot(
                ActorType: group.Key,
                ActiveCount: metrics.Length,
                MailboxQueuedSum: metrics.Sum(item => item.QueuedCount),
                MailboxQueuedMax: metrics.Length == 0 ? 0 : metrics.Max(item => item.QueuedCount),
                MailboxEnqueuedCount: metrics.Sum(item => item.EnqueuedCount),
                MailboxProcessedCount: metrics.Sum(item => item.ProcessedCount),
                MailboxRejectedCount: metrics.Sum(item => item.RejectedCount));
        })
        .OrderBy(item => item.ActorType, StringComparer.Ordinal)
        .ToArray();

    return new ActorRuntimeDiagnosticsSnapshot(actorTypes);
}
```

- [ ] **Step 5: Add session snapshot DTO and API**

Create `src/Lakona.Game.Server/Sessions/GameSessionDiagnosticsSnapshot.cs`:

```csharp
namespace Lakona.Game.Server.Sessions;

public sealed record GameSessionDiagnosticsSnapshot(
    int TotalSessions,
    int ActiveSessions,
    int ActiveConnections,
    int DisconnectedSessions,
    int TerminatedSessions,
    int ResumableSessions);
```

Modify `src/Lakona.Game.Server/Sessions/IGameSessionRegistry.cs`:

```csharp
GameSessionDiagnosticsSnapshot GetDiagnosticsSnapshot();
```

Add this method to `src/Lakona.Game.Server/Sessions/InMemoryGameSessionRegistry.cs`:

```csharp
public GameSessionDiagnosticsSnapshot GetDiagnosticsSnapshot()
{
    lock (_gate)
    {
        var states = _sessions.Values.ToArray();
        return new GameSessionDiagnosticsSnapshot(
            TotalSessions: states.Length,
            ActiveSessions: states.Count(state =>
                state.Termination is null &&
                state.ConnectionId is not null &&
                state.DisconnectedAt is null),
            ActiveConnections: _connectionToSession.Count,
            DisconnectedSessions: states.Count(state =>
                state.Termination is null &&
                state.ConnectionId is null &&
                state.DisconnectedAt is not null),
            TerminatedSessions: states.Count(state => state.Termination is not null),
            ResumableSessions: states.Count(state =>
                state.Termination is null || state.KeepTerminationForResume));
    }
}
```

- [ ] **Step 6: Run snapshot tests**

Run:

```powershell
dotnet test tests\Lakona.Game.Server.Tests\Lakona.Game.Server.Tests.csproj --no-restore --filter "FullyQualifiedName~Actor_diagnostics_snapshot|FullyQualifiedName~Session_diagnostics_snapshot"
```

Expected: pass.

- [ ] **Step 7: Commit**

```powershell
git add src/Lakona.Game.Server/Actors/IActorRuntime.cs src/Lakona.Game.Server/Actors/LakonaActorRuntime.cs src/Lakona.Game.Server/Actors/ActorRuntimeDiagnosticsSnapshot.cs src/Lakona.Game.Server/Sessions/IGameSessionRegistry.cs src/Lakona.Game.Server/Sessions/InMemoryGameSessionRegistry.cs src/Lakona.Game.Server/Sessions/GameSessionDiagnosticsSnapshot.cs tests/Lakona.Game.Server.Tests/ActorRuntimeTests.cs tests/Lakona.Game.Server.Tests/GameSessionRegistryTests.cs
git commit -m "Add safe runtime diagnostics snapshots"
```

---

### Task 7: Add Diagnostics Snapshot Providers and Endpoints

**Files:**
- Create: `src/Lakona.Game.Server/Observability/Diagnostics/ILakonaDiagnosticsSnapshotProvider.cs`
- Create: `src/Lakona.Game.Server/Observability/Diagnostics/LakonaDiagnosticsSnapshotService.cs`
- Create: `src/Lakona.Game.Server/Observability/Diagnostics/DiagnosticsSnapshotModels.cs`
- Create: `src/Lakona.Game.Server/Observability/Diagnostics/ProcessDiagnosticsProvider.cs`
- Create: `src/Lakona.Game.Server/Observability/Diagnostics/ActorDiagnosticsProvider.cs`
- Create: `src/Lakona.Game.Server/Observability/Diagnostics/SessionDiagnosticsProvider.cs`
- Create: `src/Lakona.Game.Server/Observability/Diagnostics/HotfixDiagnosticsProvider.cs`
- Create: `src/Lakona.Game.Server/Observability/Diagnostics/DiagnosticsLocalAdminRoutes.cs`
- Modify: `src/Lakona.Game.Server/Observability/LakonaObservabilityServiceCollectionExtensions.cs`
- Test: `tests/Lakona.Game.Server.Tests/Observability/DiagnosticsEndpointTests.cs`

- [ ] **Step 1: Write diagnostics endpoint tests**

Create `tests/Lakona.Game.Server.Tests/Observability/DiagnosticsEndpointTests.cs`:

```csharp
using System.Text;
using System.Text.Json;
using Lakona.Game.Server.LocalAdmin;
using Lakona.Game.Server.Observability.Diagnostics;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Lakona.Game.Server.Tests.Observability;

public sealed class DiagnosticsEndpointTests
{
    [Fact]
    public async Task Summary_endpoint_returns_bounded_json_without_sensitive_values()
    {
        var routes = DiagnosticsLocalAdminRoutes.Create(
            new LakonaDiagnosticsSnapshotService([new TestProvider()]),
            new BoundedDiagnosticsEventBuffer(10, LogLevel.Warning));
        var router = new LakonaLocalAdminRouter(routes);

        var response = await router.RouteAsync(new LakonaLocalAdminRequest(
            "GET",
            "/_lakona/diagnostics/summary",
            Stream.Null,
            RemoteAddressIsLoopback: true),
            TestContext.Current.CancellationToken);

        var json = Encoding.UTF8.GetString(response.Body);
        Assert.Equal(200, response.StatusCode);
        Assert.Contains("\"status\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret-token", json, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-session", json, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-actor", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Events_endpoint_returns_recent_events()
    {
        var buffer = new BoundedDiagnosticsEventBuffer(10, LogLevel.Warning);
        buffer.Publish(new DiagnosticsEvent(
            DateTimeOffset.UtcNow,
            LogLevel.Error,
            "Lakona.Game.Test",
            "test.failure",
            "provider failed",
            null,
            null,
            new Dictionary<string, string>()));
        var router = new LakonaLocalAdminRouter(DiagnosticsLocalAdminRoutes.Create(
            new LakonaDiagnosticsSnapshotService([]),
            buffer));

        var response = await router.RouteAsync(new LakonaLocalAdminRequest(
            "GET",
            "/_lakona/diagnostics/events",
            Stream.Null,
            RemoteAddressIsLoopback: true),
            TestContext.Current.CancellationToken);

        var json = Encoding.UTF8.GetString(response.Body);
        Assert.Equal(200, response.StatusCode);
        Assert.Contains("provider failed", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Provider_failure_returns_partial_summary_and_event()
    {
        var buffer = new BoundedDiagnosticsEventBuffer(10, LogLevel.Warning);
        var service = new LakonaDiagnosticsSnapshotService([new ThrowingProvider()], buffer);
        var router = new LakonaLocalAdminRouter(DiagnosticsLocalAdminRoutes.Create(service, buffer));

        var response = await router.RouteAsync(new LakonaLocalAdminRequest(
            "GET",
            "/_lakona/diagnostics/summary",
            Stream.Null,
            RemoteAddressIsLoopback: true),
            TestContext.Current.CancellationToken);

        var json = Encoding.UTF8.GetString(response.Body);
        Assert.Equal(200, response.StatusCode);
        Assert.Contains("partial", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ThrowingProvider", json, StringComparison.Ordinal);
        Assert.Contains(buffer.Snapshot(10), e => e.Kind == "diagnostics.provider.failure");
    }

    private sealed class TestProvider : ILakonaDiagnosticsSnapshotProvider
    {
        public string Name => "test";

        public ValueTask<object> CaptureAsync(CancellationToken cancellationToken)
        {
            return new ValueTask<object>(new { status = "ok", count = 1 });
        }
    }

    private sealed class ThrowingProvider : ILakonaDiagnosticsSnapshotProvider
    {
        public string Name => "ThrowingProvider";

        public async ValueTask<object> CaptureAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            throw new InvalidOperationException("boom");
        }
    }
}
```

- [ ] **Step 2: Run diagnostics endpoint tests and verify they fail**

Run:

```powershell
dotnet test tests\Lakona.Game.Server.Tests\Lakona.Game.Server.Tests.csproj --no-restore --filter FullyQualifiedName~DiagnosticsEndpointTests
```

Expected: compile fails because diagnostics provider/service/routes do not
exist.

- [ ] **Step 3: Add provider contract and snapshot service**

Create `src/Lakona.Game.Server/Observability/Diagnostics/ILakonaDiagnosticsSnapshotProvider.cs`:

```csharp
namespace Lakona.Game.Server.Observability.Diagnostics;

public interface ILakonaDiagnosticsSnapshotProvider
{
    string Name { get; }

    ValueTask<object> CaptureAsync(CancellationToken cancellationToken);
}
```

Create `src/Lakona.Game.Server/Observability/Diagnostics/LakonaDiagnosticsSnapshotService.cs`:

```csharp
using Microsoft.Extensions.Logging;

namespace Lakona.Game.Server.Observability.Diagnostics;

public sealed class LakonaDiagnosticsSnapshotService
{
    private readonly IReadOnlyList<ILakonaDiagnosticsSnapshotProvider> _providers;
    private readonly IDiagnosticsEventSink? _events;

    public LakonaDiagnosticsSnapshotService(
        IEnumerable<ILakonaDiagnosticsSnapshotProvider> providers,
        IDiagnosticsEventSink? events = null)
    {
        _providers = providers.ToArray();
        _events = events;
    }

    public async ValueTask<DiagnosticsSummaryResponse> CaptureSummaryAsync(
        CancellationToken cancellationToken)
    {
        var sections = new Dictionary<string, object>(StringComparer.Ordinal);
        var errors = new List<DiagnosticsProviderError>();

        foreach (var provider in _providers)
        {
            try
            {
                sections[provider.Name] = await provider.CaptureAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                errors.Add(new DiagnosticsProviderError(provider.Name, ex.GetType().Name, ex.Message));
                _events?.Publish(new DiagnosticsEvent(
                    DateTimeOffset.UtcNow,
                    LogLevel.Error,
                    "Lakona.Game.Observability",
                    "diagnostics.provider.failure",
                    $"{provider.Name} failed: {ex.Message}",
                    null,
                    null,
                    new Dictionary<string, string> { ["provider"] = provider.Name }));
            }
        }

        return new DiagnosticsSummaryResponse(
            Status: errors.Count == 0 ? "ok" : "partial",
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            Sections: sections,
            Errors: errors);
    }
}
```

Create `src/Lakona.Game.Server/Observability/Diagnostics/DiagnosticsSnapshotModels.cs`:

```csharp
namespace Lakona.Game.Server.Observability.Diagnostics;

public sealed record DiagnosticsSummaryResponse(
    string Status,
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyDictionary<string, object> Sections,
    IReadOnlyList<DiagnosticsProviderError> Errors);

public sealed record DiagnosticsProviderError(
    string Provider,
    string ErrorType,
    string Message);
```

- [ ] **Step 4: Add concrete providers**

Create `src/Lakona.Game.Server/Observability/Diagnostics/ProcessDiagnosticsProvider.cs`:

```csharp
using System.Diagnostics;

namespace Lakona.Game.Server.Observability.Diagnostics;

public sealed class ProcessDiagnosticsProvider : ILakonaDiagnosticsSnapshotProvider
{
    public string Name => "process";

    public ValueTask<object> CaptureAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var process = Process.GetCurrentProcess();
        var startedAtUtc = process.StartTime.ToUniversalTime();
        return new ValueTask<object>(new
        {
            processId = Environment.ProcessId,
            uptimeSeconds = (long)(DateTimeOffset.UtcNow - startedAtUtc).TotalSeconds,
            workingSetBytes = process.WorkingSet64,
            gcHeapBytes = GC.GetTotalMemory(forceFullCollection: false)
        });
    }
}
```

Create `src/Lakona.Game.Server/Observability/Diagnostics/ActorDiagnosticsProvider.cs`:

```csharp
using Lakona.Game.Server.Actors;

namespace Lakona.Game.Server.Observability.Diagnostics;

public sealed class ActorDiagnosticsProvider : ILakonaDiagnosticsSnapshotProvider
{
    private readonly IActorRuntime _runtime;

    public ActorDiagnosticsProvider(IActorRuntime runtime) => _runtime = runtime;

    public string Name => "actors";

    public ValueTask<object> CaptureAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new ValueTask<object>(_runtime.GetDiagnosticsSnapshot());
    }
}
```

Create `src/Lakona.Game.Server/Observability/Diagnostics/SessionDiagnosticsProvider.cs`:

```csharp
using Lakona.Game.Server.Sessions;

namespace Lakona.Game.Server.Observability.Diagnostics;

public sealed class SessionDiagnosticsProvider : ILakonaDiagnosticsSnapshotProvider
{
    private readonly IGameSessionRegistry _sessions;

    public SessionDiagnosticsProvider(IGameSessionRegistry sessions) => _sessions = sessions;

    public string Name => "sessions";

    public ValueTask<object> CaptureAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new ValueTask<object>(_sessions.GetDiagnosticsSnapshot());
    }
}
```

Create `src/Lakona.Game.Server/Observability/Diagnostics/HotfixDiagnosticsProvider.cs`:

```csharp
using Lakona.Game.Server.Hotfix;

namespace Lakona.Game.Server.Observability.Diagnostics;

public sealed class HotfixDiagnosticsProvider : ILakonaDiagnosticsSnapshotProvider
{
    private readonly IHotfixManager _hotfix;

    public HotfixDiagnosticsProvider(IHotfixManager hotfix) => _hotfix = hotfix;

    public string Name => "hotfix";

    public ValueTask<object> CaptureAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var current = _hotfix.Current;
        return new ValueTask<object>(new
        {
            current.Version,
            current.DispatchTableVersion,
            methodCount = current.Methods.Count,
            featureCount = current.Features.Count,
            lastReloadStatus = current.LastReloadStatus?.ToString(),
            current.LastFailureMessage,
            current.LastFailureExceptionType
        });
    }
}
```

- [ ] **Step 5: Add diagnostics routes**

Create `src/Lakona.Game.Server/Observability/Diagnostics/DiagnosticsLocalAdminRoutes.cs`:

```csharp
using Lakona.Game.Server.LocalAdmin;

namespace Lakona.Game.Server.Observability.Diagnostics;

public static class DiagnosticsLocalAdminRoutes
{
    public static ILakonaLocalAdminRoute Summary(LakonaDiagnosticsSnapshotService snapshots) =>
        new Route("GET", "/_lakona/diagnostics/summary",
            async (_, ct) => LakonaLocalAdminResponse.Json(await snapshots.CaptureSummaryAsync(ct).ConfigureAwait(false)));

    public static ILakonaLocalAdminRoute Netstat() =>
        new Route("GET", "/_lakona/diagnostics/netstat",
            (_, _) => new ValueTask<LakonaLocalAdminResponse>(LakonaLocalAdminResponse.Json(new
            {
                status = "unavailable",
                reason = "Transport/RPC connection accounting is not exposed by this core plan.",
                transports = Array.Empty<object>()
            })));

    public static ILakonaLocalAdminRoute Actors(LakonaDiagnosticsSnapshotService snapshots) =>
        new Route("GET", "/_lakona/diagnostics/actors",
            async (_, ct) =>
            {
                var summary = await snapshots.CaptureSummaryAsync(ct).ConfigureAwait(false);
                return LakonaLocalAdminResponse.Json(summary.Sections.TryGetValue("actors", out var actors) ? actors : new { actorTypes = Array.Empty<object>() });
            });

    public static ILakonaLocalAdminRoute Sessions(LakonaDiagnosticsSnapshotService snapshots) =>
        new Route("GET", "/_lakona/diagnostics/sessions",
            async (_, ct) =>
            {
                var summary = await snapshots.CaptureSummaryAsync(ct).ConfigureAwait(false);
                return LakonaLocalAdminResponse.Json(summary.Sections.TryGetValue("sessions", out var sessions) ? sessions : new { activeSessions = 0 });
            });

    public static ILakonaLocalAdminRoute Events(IDiagnosticsEventSink eventSink) =>
        new Route("GET", "/_lakona/diagnostics/events",
            (_, _) => new ValueTask<LakonaLocalAdminResponse>(LakonaLocalAdminResponse.Json(new { events = eventSink.Snapshot(100) })));

    public static IEnumerable<ILakonaLocalAdminRoute> Create(
        LakonaDiagnosticsSnapshotService snapshots,
        IDiagnosticsEventSink eventSink)
    {
        yield return Summary(snapshots);
        yield return Netstat();
        yield return Actors(snapshots);
        yield return Sessions(snapshots);
        yield return Events(eventSink);
    }

    private sealed class Route : ILakonaLocalAdminRoute
    {
        private readonly Func<LakonaLocalAdminRequest, CancellationToken, ValueTask<LakonaLocalAdminResponse>> _handler;

        public Route(
            string method,
            string path,
            Func<LakonaLocalAdminRequest, CancellationToken, ValueTask<LakonaLocalAdminResponse>> handler)
        {
            Method = method;
            Path = path;
            _handler = handler;
        }

        public string Method { get; }
        public string Path { get; }

        public ValueTask<LakonaLocalAdminResponse> HandleAsync(
            LakonaLocalAdminRequest request,
            CancellationToken cancellationToken) =>
            _handler(request, cancellationToken);
    }
}
```

The `netstat` response must return `status = "unavailable"` in this plan rather
than `status = "ok"`. That makes the contract explicit: the route exists and is
safe, but full transport/RPC counters require a follow-up transport diagnostics
plan before users can rely on skynet-style network statistics.

- [ ] **Step 6: Register providers and routes**

Modify `LakonaObservabilityServiceCollectionExtensions`:

```csharp
services.TryAddEnumerable(ServiceDescriptor.Singleton<ILakonaDiagnosticsSnapshotProvider, ProcessDiagnosticsProvider>());
services.TryAddEnumerable(ServiceDescriptor.Singleton<ILakonaDiagnosticsSnapshotProvider, ActorDiagnosticsProvider>());
services.TryAddEnumerable(ServiceDescriptor.Singleton<ILakonaDiagnosticsSnapshotProvider, SessionDiagnosticsProvider>());
services.TryAddEnumerable(ServiceDescriptor.Singleton<ILakonaDiagnosticsSnapshotProvider, HotfixDiagnosticsProvider>());
services.TryAddSingleton<LakonaDiagnosticsSnapshotService>();
services.AddSingleton<ILakonaLocalAdminRoute>(sp =>
    DiagnosticsLocalAdminRoutes.Summary(sp.GetRequiredService<LakonaDiagnosticsSnapshotService>()));
services.AddSingleton<ILakonaLocalAdminRoute>(_ =>
    DiagnosticsLocalAdminRoutes.Netstat());
services.AddSingleton<ILakonaLocalAdminRoute>(sp =>
    DiagnosticsLocalAdminRoutes.Actors(sp.GetRequiredService<LakonaDiagnosticsSnapshotService>()));
services.AddSingleton<ILakonaLocalAdminRoute>(sp =>
    DiagnosticsLocalAdminRoutes.Sessions(sp.GetRequiredService<LakonaDiagnosticsSnapshotService>()));
services.AddSingleton<ILakonaLocalAdminRoute>(sp =>
    DiagnosticsLocalAdminRoutes.Events(sp.GetRequiredService<IDiagnosticsEventSink>()));
```

- [ ] **Step 7: Run diagnostics endpoint tests**

Run:

```powershell
dotnet test tests\Lakona.Game.Server.Tests\Lakona.Game.Server.Tests.csproj --no-restore --filter FullyQualifiedName~DiagnosticsEndpointTests
```

Expected: pass.

- [ ] **Step 8: Commit**

```powershell
git add src/Lakona.Game.Server/Observability/Diagnostics src/Lakona.Game.Server/Observability/LakonaObservabilityServiceCollectionExtensions.cs tests/Lakona.Game.Server.Tests/Observability/DiagnosticsEndpointTests.cs
git commit -m "Add local diagnostics endpoints"
```

---

### Task 8: Sanitize Actor Activity Source and Metrics Names

**Files:**
- Modify: `src/Lakona.Game.Server/Internal/ActorKernel/Diagnostics/LakonaActorDiagnostics.cs`
- Modify: `src/Lakona.Game.Server/Internal/ActorKernel/Core/Dispatch/ActorTurnRunner.cs`
- Test: `tests/Lakona.Game.Server.Tests/Observability/ActorTraceSanitizationTests.cs`
- Test: `tests/Lakona.Game.Server.Tests/ActorRuntimeTests.cs`

- [ ] **Step 1: Write actor trace sanitization tests**

Create `tests/Lakona.Game.Server.Tests/Observability/ActorTraceSanitizationTests.cs`:

```csharp
using System.Diagnostics;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Internal.ActorKernel;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using GameActor = Lakona.Game.Server.Actors.Actor;

namespace Lakona.Game.Server.Tests.Observability;

public sealed class ActorTraceSanitizationTests
{
    [Fact]
    public async Task Actor_dispatch_activity_excludes_actor_id_and_call_chain()
    {
        using var collector = new ActivityCollector();
        await using var provider = new ServiceCollection()
            .AddLakonaGameServerActors()
            .BuildServiceProvider();
        var lifecycle = provider.GetRequiredService<IActorLifecycle>();
        var runtime = provider.GetRequiredService<IActorRuntime>();
        var id = ActorId.From("secret-actor-id");

        await lifecycle.CreateLocalAsync<TraceActor>(id, cancellationToken: TestContext.Current.CancellationToken);
        await runtime.AskAsync<TraceActor, string>(
            id,
            static (actor, ct) => actor.EchoAsync("hello", ct),
            TestContext.Current.CancellationToken);

        var activity = Assert.Single(collector.Snapshot(), item => item.OperationName.Contains("actor", StringComparison.OrdinalIgnoreCase));
        Assert.Null(activity.GetTagItem("lakona-actor.actor.id"));
        Assert.Null(activity.GetTagItem("lakona-actor.call.chain"));
        Assert.Null(activity.GetTagItem("lakona-game.actor.actor.id"));
        Assert.Null(activity.GetTagItem("lakona-game.actor.call.chain"));
        Assert.Equal(typeof(TraceActor).FullName, activity.GetTagItem("lakona-game.actor.type"));
        Assert.Equal("call", activity.GetTagItem("lakona-game.actor.message.kind"));
    }

    private sealed class TraceActor : GameActor
    {
        public ValueTask<string> EchoAsync(string value, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new ValueTask<string>(value);
        }
    }

    private sealed class ActivityCollector : IDisposable
    {
        private readonly object _gate = new();
        private readonly ActivityListener _listener;
        private readonly List<Activity> _stopped = new();

        public ActivityCollector()
        {
            _listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == LakonaActorDiagnostics.ActivitySourceName,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
                ActivityStopped = activity =>
                {
                    lock (_gate)
                    {
                        _stopped.Add(activity);
                    }
                }
            };
            ActivitySource.AddActivityListener(_listener);
        }

        public IReadOnlyList<Activity> Snapshot()
        {
            lock (_gate)
            {
                return _stopped.ToArray();
            }
        }

        public void Dispose() => _listener.Dispose();
    }
}
```

- [ ] **Step 2: Run trace test and verify it fails**

Run:

```powershell
dotnet test tests\Lakona.Game.Server.Tests\Lakona.Game.Server.Tests.csproj --no-restore --filter FullyQualifiedName~ActorTraceSanitizationTests
```

Expected: fails because current actor spans include actor id and call chain.

- [ ] **Step 3: Rename actor source and meter names**

Modify `src/Lakona.Game.Server/Internal/ActorKernel/Diagnostics/LakonaActorDiagnostics.cs`:

```csharp
public const string ActivitySourceName = "Lakona.Game.Actor";

public const string MeterName = "Lakona.Game.Actor";
```

Keep metric instrument names stable unless tests require a coordinated rename.
The source/meter name change is enough for the design's public source names.

- [ ] **Step 4: Remove sensitive actor tags**

Modify `src/Lakona.Game.Server/Internal/ActorKernel/Core/Dispatch/ActorTurnRunner.cs`.

Replace:

```csharp
activity?.SetTag("lakona-actor.actor.id", self.Id.Value);
activity?.SetTag("lakona-actor.message.type", messageType);
activity?.SetTag("lakona-actor.message.kind", envelope.Response is null ? "send" : "call");
activity?.SetTag("lakona-actor.call.chain", string.Join(">", callChain.Select(id => id.Value)));
```

with:

```csharp
activity?.SetTag("lakona-game.actor.type", actor.GetType().FullName ?? actor.GetType().Name);
activity?.SetTag("lakona-game.actor.message.type", messageType);
activity?.SetTag("lakona-game.actor.message.kind", envelope.Response is null ? "send" : "call");
```

Do not add actor id, actor name, session id, route key, payload, request value,
or call chain tags.

- [ ] **Step 5: Run trace tests**

Run:

```powershell
dotnet test tests\Lakona.Game.Server.Tests\Lakona.Game.Server.Tests.csproj --no-restore --filter FullyQualifiedName~ActorTraceSanitizationTests
```

Expected: pass.

- [ ] **Step 6: Commit**

```powershell
git add src/Lakona.Game.Server/Internal/ActorKernel/Diagnostics/LakonaActorDiagnostics.cs src/Lakona.Game.Server/Internal/ActorKernel/Core/Dispatch/ActorTurnRunner.cs tests/Lakona.Game.Server.Tests/Observability/ActorTraceSanitizationTests.cs
git commit -m "Sanitize actor tracing"
```

---

### Task 9: Configure Framework Logging from Observability Options

**Files:**
- Create: `src/Lakona.Game.Server/Observability/LakonaLoggingConfiguration.cs`
- Modify: `src/Lakona.Game.Server/Hosting/LakonaGameServer.cs`
- Test: `tests/Lakona.Game.Server.Tests/Observability/LakonaObservabilityOptionsTests.cs`

- [ ] **Step 1: Add log level parsing tests**

Add to `LakonaObservabilityOptionsTests`:

```csharp
[Fact]
public void Logging_configuration_applies_minimum_and_category_levels()
{
    var configuration = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Lakona:Observability:Logging:MinimumLevel"] = "Warning",
            ["Lakona:Observability:Logging:Categories:Lakona.Game.Actor"] = "Debug"
        })
        .Build();
    var options = LakonaObservabilityOptions.FromConfiguration(
        configuration,
        LakonaGameRuntimeProfile.Development);

    Assert.Equal("Warning", options.Logging.MinimumLevel);
    Assert.Equal("Debug", options.Logging.Categories["Lakona.Game.Actor"]);
}
```

- [ ] **Step 2: Add logging configuration helper**

Create `src/Lakona.Game.Server/Observability/LakonaLoggingConfiguration.cs`:

```csharp
using Lakona.Game.Server.Configuration;
using Microsoft.Extensions.Logging;

namespace Lakona.Game.Server.Observability;

internal static class LakonaLoggingConfiguration
{
    public static void Apply(
        ILoggingBuilder logging,
        LakonaObservabilityLoggingOptions options)
    {
        logging.ClearProviders();
        if (!options.Enabled)
        {
            return;
        }

        logging.SetMinimumLevel(ParseLevel(options.MinimumLevel));
        foreach (var category in options.Categories)
        {
            logging.AddFilter(category.Key, ParseLevel(category.Value));
        }

        if (options.Console.Enabled)
        {
            logging.AddSimpleConsole(console =>
            {
                console.SingleLine = string.Equals(options.Console.Format, "Compact", StringComparison.OrdinalIgnoreCase);
                console.IncludeScopes = options.Console.IncludeScopes;
                console.TimestampFormat = "HH:mm:ss ";
            });
        }
    }

    private static LogLevel ParseLevel(string value) =>
        Enum.TryParse<LogLevel>(value, ignoreCase: true, out var level)
            ? level
            : LogLevel.Information;
}
```

Invalid levels are caught by guardrails. The helper uses a conservative fallback
only so startup logging setup does not throw before validation can report stable
codes.

- [ ] **Step 3: Use helper during host startup**

Modify `src/Lakona.Game.Server/Hosting/LakonaGameServer.cs`:

```csharp
var profile = LakonaGameRuntimeProfileResolver.Resolve(
    builder.Configuration,
    builder.Environment.EnvironmentName);
var runtimeOptions = LakonaGameRuntimeOptions.FromConfiguration(
    builder.Configuration,
    builder.Environment.EnvironmentName);

LakonaLoggingConfiguration.Apply(builder.Logging, runtimeOptions.Observability.Logging);
```

Remove the unconditional:

```csharp
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
```

- [ ] **Step 4: Run option and hosting tests**

Run:

```powershell
dotnet test tests\Lakona.Game.Server.Tests\Lakona.Game.Server.Tests.csproj --no-restore --filter "FullyQualifiedName~LakonaObservabilityOptionsTests|FullyQualifiedName~LakonaGameServerHostingOptionsTests"
```

Expected: pass.

- [ ] **Step 5: Commit**

```powershell
git add src/Lakona.Game.Server/Observability/LakonaLoggingConfiguration.cs src/Lakona.Game.Server/Hosting/LakonaGameServer.cs tests/Lakona.Game.Server.Tests/Observability/LakonaObservabilityOptionsTests.cs
git commit -m "Apply observability logging options"
```

---

### Task 10: Wire Startup Validation for Observability

**Files:**
- Modify: `src/Lakona.Game.Server/Hosting/LakonaGameServer.cs`
- Modify: `src/Lakona.Game.Server/Health/LakonaGameReadinessProbe.cs`
- Test: `tests/Lakona.Game.Server.Tests/Health/LakonaGameReadinessProbeTests.cs`
- Test: `tests/Lakona.Game.Server.Tests/Guardrails/LakonaGameRuntimeValidatorTests.cs`

- [ ] **Step 1: Write startup validation helper tests**

Add to `LakonaGameRuntimeValidatorTests`:

```csharp
[Fact]
public void RuntimeValidator_includes_observability_rule_by_default()
{
    var services = new ServiceCollection();

    services.AddLakonaGameRuntimeValidation();

    using var provider = services.BuildServiceProvider();
    var rules = provider.GetServices<ILakonaGameValidationRule>().ToArray();

    Assert.Contains(rules, rule => rule.GetType().Name == "ObservabilityRule");
}
```

- [ ] **Step 2: Add runtime validation before full startup listeners**

In `LakonaGameServer.RunAsync`, after services are configured but before
`builder.Build()` starts hosted services, build a resolved runtime model and run
`LakonaGameRuntimeValidator`. Use the same conversion path as readiness.

Add internal helper:

```csharp
internal static LakonaGameValidationResult ValidateRuntimeForTesting(
    LakonaGameRuntimeOptions runtimeOptions,
    ClusterOptions? clusterOptions,
    LakonaObservabilityCapabilities? observabilityCapabilities = null)
{
    var rules = new ILakonaGameValidationRule[]
    {
        new NodeIdentityRule(),
        new EndpointRule(),
        new HotfixSourceRule(),
        new ObservabilityRule(),
        new ClusterEndpointRule()
    };
    var resolved = LakonaGameReadinessProbe.ToResolvedRuntimeForTesting(
        runtimeOptions,
        clusterOptions,
        observabilityCapabilities ?? new LakonaObservabilityCapabilities());
    return new LakonaGameRuntimeValidator(rules).Validate(resolved);
}
```

If `result.Succeeded` is false, log each diagnostic and throw:

```csharp
throw new InvalidOperationException(
    $"Lakona runtime validation failed with {result.Diagnostics.Count(d => d.Severity == LakonaGameDiagnosticSeverity.Error)} error(s). First error: {first.Code} {first.Message}");
```

- [ ] **Step 3: Expose resolved runtime conversion for tests**

In `LakonaGameReadinessProbe`, make the conversion internal:

```csharp
internal static LakonaGameResolvedRuntime ToResolvedRuntimeForTesting(
    LakonaGameRuntimeOptions runtime,
    ClusterOptions? clusterOptions,
    LakonaObservabilityCapabilities capabilities)
{
    return ToResolvedRuntime(runtime, clusterOptions, capabilities);
}
```

- [ ] **Step 4: Run validator/readiness tests**

Run:

```powershell
dotnet test tests\Lakona.Game.Server.Tests\Lakona.Game.Server.Tests.csproj --no-restore --filter "FullyQualifiedName~RuntimeValidator_includes_observability_rule_by_default|FullyQualifiedName~Readiness"
```

Expected: pass.

- [ ] **Step 5: Commit**

```powershell
git add src/Lakona.Game.Server/Hosting/LakonaGameServer.cs src/Lakona.Game.Server/Health/LakonaGameReadinessProbe.cs tests/Lakona.Game.Server.Tests/Health/LakonaGameReadinessProbeTests.cs tests/Lakona.Game.Server.Tests/Guardrails/LakonaGameRuntimeValidatorTests.cs
git commit -m "Validate observability before startup"
```

---

### Task 11: Update Durable Documentation

**Files:**
- Modify: `docs/configuration.md`
- Modify: `docs/guardrails.md`
- Modify: `docs/actor.md`
- Modify: `docs/hotfix/architecture.md`
- Modify: `src/Lakona.Game.Server/README.md`

- [ ] **Step 1: Update configuration documentation**

Add a `Lakona:Observability` section to `docs/configuration.md` with:

````markdown
## Observability

`Lakona:Observability` controls Lakona-owned logging defaults, local diagnostics,
the local admin endpoint, and exporter endpoints. Framework instrumentation uses
`ILogger`, `Meter`, and `ActivitySource` even when exporters are disabled.

`LocalAdmin.Enabled` defaults from `Lakona:Profile`, not directly from
`DOTNET_ENVIRONMENT`:

- `Development`: enabled on loopback.
- `Compose`: disabled unless explicitly enabled.
- `Production`: disabled unless explicitly enabled.

```json
{
  "Lakona": {
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
        "Enabled": null,
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
````

- [ ] **Step 2: Update guardrails documentation**

Add `ULINK130-ULINK139` to `docs/guardrails.md` under diagnostic codes and
describe startup/readiness behavior. Include:

```txt
ULINK130 error Observability local admin host must bind to loopback.
ULINK131 warning Diagnostics detail mode is enabled.
ULINK132 error Diagnostics detail mode cannot be exposed on non-loopback local admin.
ULINK133 error File logging requires a registered file logging integration.
ULINK134 error Tracing export requires a registered OpenTelemetry integration.
ULINK135 error Prometheus metrics endpoint requires a registered endpoint implementation.
ULINK136 error Observability metrics path is invalid.
ULINK137 error Observability event buffer capacity is invalid.
ULINK138 error Observability log level is invalid.
ULINK139 error Observability trace sample rate is invalid.
```

- [ ] **Step 3: Update actor documentation**

Add this section to `docs/actor.md` near the existing diagnostics/runtime
observability material:

```markdown
## Actor Diagnostics Privacy

Lakona actor diagnostics expose aggregate actor type counts and mailbox
counters by default. Default diagnostics JSON, metric tags, and trace
attributes must not include actor ids, actor names, call chains, message
payloads, request values, session ids, tokens, or user-specific identifiers.

Default local diagnostics may include low-cardinality fields such as actor type,
message type, timeout reason, mailbox queue totals, processed counts, rejected
counts, and slow-message counters. Detail endpoints that expose actor-specific
state are disabled by default and require explicit diagnostics detail mode.
```

- [ ] **Step 4: Update hotfix documentation**

In `docs/hotfix/architecture.md`, replace the hotfix-admin-only listener
description with:

````markdown
Hotfix operations are exposed as route modules under Lakona's loopback local
admin host:

```txt
GET  /_lakona/hotfix/status
POST /_lakona/hotfix/activate
POST /_lakona/hotfix/rollback
POST /_lakona/hotfix/reload
```

Production hotfix package mode is independent of whether the local admin
listener is enabled. Production mode selects the version pointer source under
`hotfix/versions` and `current.txt`. If local admin is disabled, startup can
still load `current.txt`, but online `activate`, `status`, `rollback`, and
`reload` commands are unavailable. Operators who need online hotfix operations
must explicitly enable local admin on loopback.
````

- [ ] **Step 5: Update package README**

In `src/Lakona.Game.Server/README.md`, add a concise user-facing section:

```markdown
## Observability

Lakona emits logs, metrics, and traces through standard .NET diagnostics.
Development servers enable the loopback local admin diagnostics endpoint by
default. Production profiles keep local admin disabled unless explicitly
enabled.
```

- [ ] **Step 6: Run docs consistency scan**

Run:

```powershell
pwsh -NoProfile -File scripts/rpc/check-docs-consistency.ps1
```

Expected: pass. If the script reports unrelated existing issues, capture them
in the final implementation notes and run the narrower tests from the touched
areas.

- [ ] **Step 7: Commit**

```powershell
git add docs/configuration.md docs/guardrails.md docs/actor.md docs/hotfix/architecture.md src/Lakona.Game.Server/README.md
git commit -m "Document observability configuration"
```

---

### Task 12: Bump Versions and Run Focused Validation

**Files:**
- Modify: `src/Lakona.Game.Server/Lakona.Game.Server.csproj`
- Modify: generated package version references only if tests prove they are
  needed.

- [ ] **Step 1: Bump shippable package version**

Because this plan changes shippable code under `src/Lakona.Game.Server`, bump:

```xml
<Version>0.8.20</Version>
```

in `src/Lakona.Game.Server/Lakona.Game.Server.csproj`.

Do not bump packages that were not modified under `src/**`.

- [ ] **Step 2: Run focused server tests**

Run:

```powershell
dotnet test tests\Lakona.Game.Server.Tests\Lakona.Game.Server.Tests.csproj --no-restore
```

Expected: pass.

- [ ] **Step 3: Run cluster diagnostics tests**

Run:

```powershell
dotnet test tests\Lakona.Game.Cluster.Tests\Lakona.Game.Cluster.Tests.csproj --no-restore --filter FullyQualifiedName~ClusterDiagnosticsTests
```

Expected: pass. These tests protect low-cardinality diagnostics in lower
packages after the server-side observability work.

- [ ] **Step 4: Build solution without restore**

Run:

```powershell
dotnet build Lakona.slnx --no-restore
```

Expected: pass. Existing NuGet vulnerability warnings may appear; do not treat
pre-existing warnings as failures unless the build exits non-zero.

- [ ] **Step 5: Commit version bump**

```powershell
git add src/Lakona.Game.Server/Lakona.Game.Server.csproj
git commit -m "Bump game server package for observability"
```

---

## Final Review Checklist

- [ ] `docs/superpowers/specs/2026-06-30-observability-design.md` requirements
  are covered by tasks or explicitly deferred to follow-up optional integration
  plans.
- [ ] Local admin defaults use `LakonaGameRuntimeProfile`, not raw
  `DOTNET_ENVIRONMENT`.
- [ ] `DOTNET_ENVIRONMENT=battle-1` does not select development local admin
  defaults unless `Lakona:Profile=Development` is explicitly configured.
- [ ] No lower package depends on `Lakona.Game.Server` for diagnostic events.
- [ ] Production hotfix package mode no longer depends on local admin listener
  enablement.
- [ ] Default diagnostics JSON excludes actor ids, session ids, connection ids,
  tokens, call chains, payloads, request values, and user-specific identifiers.
- [ ] Actor dead letter, slow message, and call timeout events enter the event
  buffer without actor ids, call chains, request payloads, or message payloads.
- [ ] Lakona warning/error log events enter the event buffer without rendering
  message templates, structured values, tokens, payloads, or user identifiers.
- [ ] Actor spans exclude actor ids and call chains.
- [ ] `/_lakona/diagnostics/netstat` is either backed by real transport/RPC
  counters or explicitly returns `status = "unavailable"` until the follow-up
  transport diagnostics plan is implemented.
- [ ] Observability guardrail codes appear in startup and readiness JSON/text.
- [ ] `docs/guardrails.md` reserves `ULINK130-ULINK139`.
- [ ] Modified shippable package versions are bumped.

## Handoff Notes

Use `superpowers:subagent-driven-development` for implementation. Suggested
parallelization:

- Worker A: Tasks 1-2, options/profile/guardrails/readiness.
- Worker B: Tasks 3-4, local admin and hotfix route migration.
- Worker C: Tasks 5-7, event buffer and diagnostics endpoints.
- Worker D: Tasks 8-12, actor trace sanitization, docs, version, validation.

Do not let workers share write ownership for the same file in the same round.
`LakonaGameServer.cs`, `LakonaGameReadinessProbe.cs`, and
`LakonaObservabilityServiceCollectionExtensions.cs` should be integrated by the
main agent or assigned to only one worker at a time.
