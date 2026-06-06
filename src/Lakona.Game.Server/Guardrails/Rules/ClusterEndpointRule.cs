namespace Lakona.Game.Server.Guardrails.Rules;

public sealed class ClusterEndpointRule : ILakonaGameValidationRule
{
    public IEnumerable<LakonaGameDiagnostic> Validate(LakonaGameResolvedRuntime runtime)
    {
        if (runtime.ClusterEndpoint is null)
        {
            yield break;
        }

        if (string.IsNullOrWhiteSpace(runtime.ClusterEndpoint.Endpoint.Value))
        {
            yield return new LakonaGameDiagnostic(
                "ULINK040",
                LakonaGameDiagnosticSeverity.Error,
                "Lakona.Game:Cluster:Endpoint is required when Cluster is configured.",
                "Set Lakona.Game:Cluster:Endpoint to a URI such as tcp://127.0.0.1:21001.");
            yield break;
        }

        if (!Uri.TryCreate(runtime.ClusterEndpoint.Endpoint.Value, UriKind.Absolute, out var uri)
            || !IsSupportedTcpUri(uri))
        {
            yield return new LakonaGameDiagnostic(
                "ULINK043",
                LakonaGameDiagnosticSeverity.Error,
                "Lakona.Game:Cluster:Endpoint must be a tcp URI with host and explicit port.",
                "Use a value such as tcp://127.0.0.1:21001.");
            yield break;
        }

        foreach (var endpoint in runtime.Endpoints)
        {
            if (endpoint.Port.Value == uri.Port)
            {
                yield return new LakonaGameDiagnostic(
                    "ULINK042",
                    LakonaGameDiagnosticSeverity.Error,
                    $"Cluster endpoint port {uri.Port} conflicts with a business endpoint.",
                    "Use a different port for Lakona.Game:Cluster:Endpoint.");
            }
        }
    }

    private static bool IsSupportedTcpUri(Uri uri)
    {
        return string.Equals(uri.Scheme, "tcp", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(uri.Host)
            && !uri.IsDefaultPort
            && uri.Port is >= 1 and <= 65535;
    }
}
