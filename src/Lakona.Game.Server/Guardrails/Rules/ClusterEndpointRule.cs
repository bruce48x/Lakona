using Lakona.Game.Server.Configuration;

namespace Lakona.Game.Server.Guardrails.Rules;

internal static class ClusterEndpointRule
{
    internal static IEnumerable<LakonaGameDiagnostic> Validate(LakonaGameRuntimeOptions runtime)
    {
        if (string.IsNullOrWhiteSpace(runtime.Cluster.Endpoint))
        {
            yield return new LakonaGameDiagnostic(
                "LAKONA10040",
                LakonaGameDiagnosticSeverity.Error,
                "Lakona:Cluster:Endpoint is required when Cluster is configured.",
                "Set Lakona:Cluster:Endpoint to a URI such as tcp://127.0.0.1:21001.");
            yield break;
        }

        if (!Uri.TryCreate(runtime.Cluster.Endpoint, UriKind.Absolute, out var uri)
            || !IsSupportedClusterUri(uri))
        {
            yield return new LakonaGameDiagnostic(
                "LAKONA10043",
                LakonaGameDiagnosticSeverity.Error,
                "Lakona:Cluster:Endpoint must be an absolute URI with a scheme, host, and explicit port.",
                "Use a value such as tcp://127.0.0.1:21001.");
            yield break;
        }

        foreach (var endpoint in runtime.Endpoints)
        {
            if (endpoint.Port == uri.Port)
            {
                yield return new LakonaGameDiagnostic(
                    "LAKONA10042",
                    LakonaGameDiagnosticSeverity.Error,
                    $"Cluster endpoint port {uri.Port} conflicts with a business endpoint.",
                    "Use a different port for Lakona:Cluster:Endpoint.");
            }
        }
    }

    private static bool IsSupportedClusterUri(Uri uri)
    {
        return !string.IsNullOrWhiteSpace(uri.Scheme)
            && !string.IsNullOrWhiteSpace(uri.Host)
            && !uri.IsDefaultPort
            && uri.Port is >= 1 and <= 65535;
    }
}
