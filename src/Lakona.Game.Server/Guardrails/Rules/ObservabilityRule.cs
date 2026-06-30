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
                "Lakona:Observability:LocalAdmin:Host binds local admin to a non-loopback host while Lakona:Observability:LocalAdmin:RequireLoopback is true.",
                "Set Lakona:Observability:LocalAdmin:Host to 127.0.0.1, localhost, or ::1, or disable Lakona:Observability:LocalAdmin:RequireLoopback only in trusted local environments.");
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

        if (!IsValidPrometheusPath(observability.PrometheusPath.Value))
        {
            yield return new LakonaGameDiagnostic(
                "ULINK136",
                LakonaGameDiagnosticSeverity.Error,
                "Lakona:Observability:Metrics:Prometheus:Path must be an absolute non-root path without query or fragment.",
                "Use an absolute non-root path such as /_lakona/metrics without query or fragment.");
        }

        if (observability.EventBufferCapacity.Value <= 0)
        {
            yield return new LakonaGameDiagnostic(
                "ULINK137",
                LakonaGameDiagnosticSeverity.Error,
                "Lakona:Observability:Diagnostics:EventBuffer:Capacity must be greater than zero.",
                "Set Lakona:Observability:Diagnostics:EventBuffer:Capacity to a positive integer.");
        }

        if (!Enum.TryParse<LogLevel>(
                observability.LoggingMinimumLevel.Value,
                ignoreCase: true,
                out var loggingMinimumLevel)
            || !Enum.IsDefined(typeof(LogLevel), loggingMinimumLevel))
        {
            yield return new LakonaGameDiagnostic(
                "ULINK138",
                LakonaGameDiagnosticSeverity.Error,
                "Lakona:Observability:Logging:MinimumLevel is invalid.",
                "Use Trace, Debug, Information, Warning, Error, Critical, or None.");
        }

        if (observability.TraceSampleRate.Value is < 0.0 or > 1.0)
        {
            yield return new LakonaGameDiagnostic(
                "ULINK139",
                LakonaGameDiagnosticSeverity.Error,
                "Lakona:Observability:Tracing:Export:SampleRate must be between 0.0 and 1.0.",
                "Set Lakona:Observability:Tracing:Export:SampleRate between 0.0 and 1.0.");
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
            && path.Length > 1
            && !path.Contains('?', StringComparison.Ordinal)
            && !path.Contains('#', StringComparison.Ordinal);
    }
}
