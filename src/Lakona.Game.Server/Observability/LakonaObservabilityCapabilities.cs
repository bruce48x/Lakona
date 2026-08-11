namespace Lakona.Game.Server.Observability;

public interface ILakonaObservabilityCapability
{
    LakonaObservabilityCapabilityKind Kind { get; }
}

public enum LakonaObservabilityCapabilityKind
{
    OpenTelemetry,
    PrometheusEndpoint
}

public sealed record LakonaObservabilityCapabilities(
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
            OpenTelemetryIntegrationRegistered: kinds.Contains(LakonaObservabilityCapabilityKind.OpenTelemetry),
            PrometheusEndpointRegistered: kinds.Contains(LakonaObservabilityCapabilityKind.PrometheusEndpoint));
    }
}

public sealed class OpenTelemetryObservabilityCapability : ILakonaObservabilityCapability
{
    public LakonaObservabilityCapabilityKind Kind => LakonaObservabilityCapabilityKind.OpenTelemetry;
}

public sealed class PrometheusEndpointObservabilityCapability : ILakonaObservabilityCapability
{
    public LakonaObservabilityCapabilityKind Kind => LakonaObservabilityCapabilityKind.PrometheusEndpoint;
}
