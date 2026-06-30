using Microsoft.Extensions.Logging;

namespace Lakona.Game.Server.Observability.Diagnostics;

public sealed class LakonaDiagnosticsSnapshotService
{
    private const string ProviderFailureMessage = "Diagnostics provider failed.";
    private readonly IReadOnlyList<ILakonaDiagnosticsSnapshotProvider> _providers;
    private readonly IDiagnosticsEventSink? _events;

    public LakonaDiagnosticsSnapshotService(
        IEnumerable<ILakonaDiagnosticsSnapshotProvider> providers,
        IDiagnosticsEventSink? events = null)
    {
        ArgumentNullException.ThrowIfNull(providers);

        _providers = providers.ToArray();
        _events = events;
    }

    public async ValueTask<DiagnosticsSummaryResponse> CaptureSummaryAsync(CancellationToken cancellationToken = default)
    {
        var sections = new Dictionary<string, object>(StringComparer.Ordinal);
        var errors = new List<DiagnosticsProviderError>();

        foreach (var provider in _providers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                sections[provider.Name] = await provider.CaptureAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                errors.Add(new DiagnosticsProviderError(
                    provider.Name,
                    exception.GetType().Name,
                    ProviderFailureMessage));
                PublishProviderFailure(provider.Name);
            }
        }

        return new DiagnosticsSummaryResponse(
            errors.Count == 0 ? "ok" : "partial",
            DateTimeOffset.UtcNow,
            sections,
            errors);
    }

    private void PublishProviderFailure(string provider)
    {
        _events?.Publish(new DiagnosticsEvent(
            DateTimeOffset.UtcNow,
            LogLevel.Error,
            "Lakona.Game.Observability",
            "diagnostics.provider.failure",
            ProviderFailureMessage,
            TraceId: null,
            CorrelationId: null,
            new Dictionary<string, string?> { ["provider"] = provider }));
    }
}
