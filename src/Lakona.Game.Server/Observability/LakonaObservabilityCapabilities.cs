namespace Lakona.Game.Server.Observability;

public interface ILakonaObservabilityCapability
{
    LakonaObservabilityCapabilityKind Kind { get; }
}

public enum LakonaObservabilityCapabilityKind
{
    FileLogging,
    OpenTelemetry,
    PrometheusEndpoint
}

public sealed record LakonaObservabilityCapabilities(
    bool FileLogging,
    bool OpenTelemetry,
    bool PrometheusEndpoint)
{
    public static LakonaObservabilityCapabilities FromServices(
        IEnumerable<ILakonaObservabilityCapability> capabilities)
    {
        var kinds = capabilities
            .Select(capability => capability.Kind)
            .ToHashSet();

        return new LakonaObservabilityCapabilities(
            FileLogging: kinds.Contains(LakonaObservabilityCapabilityKind.FileLogging),
            OpenTelemetry: kinds.Contains(LakonaObservabilityCapabilityKind.OpenTelemetry),
            PrometheusEndpoint: kinds.Contains(LakonaObservabilityCapabilityKind.PrometheusEndpoint));
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
