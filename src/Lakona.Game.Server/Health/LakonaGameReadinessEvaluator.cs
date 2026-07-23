using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.Guardrails;
using Lakona.Game.Server.Observability;

namespace Lakona.Game.Server.Health;

public sealed class LakonaGameReadinessEvaluator
{
    private readonly LakonaGameRuntimeOptions _runtime;
    private readonly ClusterOptions _clusterOptions;
    private readonly LakonaObservabilityCapabilities _observabilityCapabilities;
    private readonly LakonaHealthReadinessState _readinessState;
    private readonly LakonaGameRuntimeValidator _validator;
    private readonly LakonaServerReadinessState? _serverReadiness;

    public LakonaGameReadinessEvaluator(
        LakonaGameRuntimeOptions runtime,
        ClusterOptions clusterOptions,
        LakonaObservabilityCapabilities observabilityCapabilities,
        LakonaHealthReadinessState readinessState,
        LakonaGameRuntimeValidator validator)
        : this(
            runtime,
            clusterOptions,
            observabilityCapabilities,
            readinessState,
            validator,
            serverReadiness: null)
    {
    }

    internal LakonaGameReadinessEvaluator(
        LakonaGameRuntimeOptions runtime,
        ClusterOptions clusterOptions,
        LakonaObservabilityCapabilities observabilityCapabilities,
        LakonaHealthReadinessState readinessState,
        LakonaGameRuntimeValidator validator,
        LakonaServerReadinessState? serverReadiness)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _clusterOptions = clusterOptions ?? throw new ArgumentNullException(nameof(clusterOptions));
        _observabilityCapabilities = observabilityCapabilities ?? throw new ArgumentNullException(nameof(observabilityCapabilities));
        _readinessState = readinessState ?? throw new ArgumentNullException(nameof(readinessState));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _serverReadiness = serverReadiness;
    }

    public LakonaGameReadinessSnapshot Evaluate()
    {
        var resolved = LakonaGameReadinessRuntime.ToResolvedRuntimeForValidation(
            _runtime,
            _clusterOptions,
            _observabilityCapabilities,
            _readinessState.HotfixAssemblyPath);
        var result = _validator.Validate(resolved);
        var diagnostics = result.Diagnostics
            .Concat(_serverReadiness?.Diagnostics ?? [])
            .ToArray();
        return new LakonaGameReadinessSnapshot(
            diagnostics.All(static diagnostic =>
                diagnostic.Severity != LakonaGameDiagnosticSeverity.Error),
            diagnostics);
    }
}

public sealed record LakonaGameReadinessSnapshot(
    bool Succeeded,
    IReadOnlyList<LakonaGameDiagnostic> Diagnostics);

public sealed record LakonaHealthReadinessState(string HotfixAssemblyPath)
{
    public static LakonaHealthReadinessState Defaults()
    {
        return new LakonaHealthReadinessState(
            Path.Combine(AppContext.BaseDirectory, "hotfix", "Server.Hotfix.dll"));
    }
}
