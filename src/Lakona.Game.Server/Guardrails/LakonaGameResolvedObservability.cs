namespace Lakona.Game.Server.Guardrails;

public sealed record LakonaGameResolvedObservability(
    LakonaGameResolvedValue<bool> LocalAdminEnabled,
    LakonaGameResolvedValue<string> ManagementHttpHost,
    LakonaGameResolvedValue<bool> LocalAdminRequireLoopback,
    LakonaGameResolvedValue<bool> DetailEnabled,
    LakonaGameResolvedValue<bool> TraceExportEnabled,
    bool OpenTelemetryIntegrationRegistered,
    LakonaGameResolvedValue<bool> PrometheusEnabled,
    bool PrometheusEndpointRegistered,
    LakonaGameResolvedValue<string> PrometheusPath,
    LakonaGameResolvedValue<int> EventBufferCapacity,
    LakonaGameResolvedValue<string> EventBufferCapacityRaw,
    LakonaGameResolvedValue<double> TraceSampleRate,
    LakonaGameResolvedValue<string> TraceSampleRateRaw);
