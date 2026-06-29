namespace Lakona.Game.Server.Observability;

public sealed record LakonaObservabilityCapabilities(
    bool FileLogging,
    bool OpenTelemetry,
    bool Prometheus)
{
    public static LakonaObservabilityCapabilities FromOptions(LakonaObservabilityOptions options)
    {
        var prometheus = options.Metrics.Prometheus.Enabled;
        var traceExport = options.Tracing.Export.Enabled;

        return new LakonaObservabilityCapabilities(
            FileLogging: options.Logging.File.Enabled,
            OpenTelemetry: prometheus || traceExport,
            Prometheus: prometheus);
    }
}
