using System.Net;
using Microsoft.Extensions.Logging;

namespace Lakona.Game.Server.Guardrails.Rules;

public sealed class ObservabilityRule : ILakonaGameValidationRule
{
    public IEnumerable<LakonaGameDiagnostic> Validate(LakonaGameResolvedRuntime runtime)
    {
        var observability = runtime.Observability;
        var localAdminIsLoopback = IsLoopbackHost(observability.LocalAdminHost.Value);

        if (observability.LocalAdminEnabled.Value
            && observability.LocalAdminRequireLoopback.Value
            && !localAdminIsLoopback)
        {
            yield return new LakonaGameDiagnostic(
                "ULINK130",
                LakonaGameDiagnosticSeverity.Error,
                "Local admin is enabled with loopback required but binds to a non-loopback host.",
                "Lakona:Observability:LocalAdmin:Host");
        }

        if (observability.DetailEnabled.Value)
        {
            yield return new LakonaGameDiagnostic(
                "ULINK131",
                LakonaGameDiagnosticSeverity.Warning,
                "Detailed diagnostics are enabled and may expose sensitive runtime state.",
                "Set Lakona:Observability:Diagnostics:DetailEnabled to false outside trusted local development.");
        }

        if (observability.DetailEnabled.Value
            && observability.LocalAdminEnabled.Value
            && !localAdminIsLoopback)
        {
            yield return new LakonaGameDiagnostic(
                "ULINK132",
                LakonaGameDiagnosticSeverity.Error,
                "Detailed diagnostics are exposed through non-loopback local admin.",
                "Bind Lakona:Observability:LocalAdmin:Host to localhost, 127.0.0.1, or ::1.");
        }

        if (observability.FileLoggingEnabled.Value
            && !observability.FileLoggingIntegrationRegistered)
        {
            yield return new LakonaGameDiagnostic(
                "ULINK133",
                LakonaGameDiagnosticSeverity.Error,
                "File logging is enabled but no file logging integration is registered.",
                "Disable Lakona:Observability:Logging:File:Enabled or register a file logging integration.");
        }

        if (observability.TraceExportEnabled.Value
            && !observability.OpenTelemetryIntegrationRegistered)
        {
            yield return new LakonaGameDiagnostic(
                "ULINK134",
                LakonaGameDiagnosticSeverity.Error,
                "Trace export is enabled but no OpenTelemetry integration is registered.",
                "Disable Lakona:Observability:Tracing:Export:Enabled or register an OpenTelemetry integration.");
        }

        if (observability.PrometheusEnabled.Value
            && !observability.PrometheusEndpointRegistered)
        {
            yield return new LakonaGameDiagnostic(
                "ULINK135",
                LakonaGameDiagnosticSeverity.Error,
                "Prometheus metrics are enabled but no Prometheus endpoint integration is registered.",
                "Disable Lakona:Observability:Metrics:Prometheus:Enabled or register a Prometheus endpoint integration.");
        }

        if (observability.PrometheusEnabled.Value
            && !IsValidPrometheusPath(observability.PrometheusPath.Value))
        {
            yield return new LakonaGameDiagnostic(
                "ULINK136",
                LakonaGameDiagnosticSeverity.Error,
                "Prometheus path must be an absolute path without query or fragment.",
                "Lakona:Observability:Metrics:Prometheus:Path");
        }

        if (observability.EventBufferCapacity.Value <= 0)
        {
            yield return new LakonaGameDiagnostic(
                "ULINK137",
                LakonaGameDiagnosticSeverity.Error,
                "Diagnostics event buffer capacity must be greater than zero.",
                "Lakona:Observability:Diagnostics:EventBuffer:Capacity");
        }

        if (!Enum.IsDefined(typeof(LogLevel), observability.LoggingMinimumLevel.Value))
        {
            yield return new LakonaGameDiagnostic(
                "ULINK138",
                LakonaGameDiagnosticSeverity.Error,
                "Logging minimum level is invalid.",
                "Lakona:Observability:Logging:MinimumLevel");
        }

        if (observability.TraceSampleRate.Value is < 0.0 or > 1.0)
        {
            yield return new LakonaGameDiagnostic(
                "ULINK139",
                LakonaGameDiagnosticSeverity.Error,
                "Trace sample rate must be between 0.0 and 1.0.",
                "Lakona:Observability:Tracing:Export:SampleRate");
        }
    }

    private static bool IsLoopbackHost(string host)
    {
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return IPAddress.TryParse(host, out var address)
            && IPAddress.IsLoopback(address);
    }

    private static bool IsValidPrometheusPath(string path)
    {
        return !string.IsNullOrWhiteSpace(path)
            && path.StartsWith("/", StringComparison.Ordinal)
            && !path.Contains('?', StringComparison.Ordinal)
            && !path.Contains('#', StringComparison.Ordinal);
    }
}
