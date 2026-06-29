using Microsoft.Extensions.Logging;

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
    LakonaGameResolvedValue<LogLevel> LoggingMinimumLevel,
    LakonaGameResolvedValue<double> TraceSampleRate);
