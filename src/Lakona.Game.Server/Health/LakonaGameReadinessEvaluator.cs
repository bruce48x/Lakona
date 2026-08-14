using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.Guardrails;
using Lakona.Game.Server.Hosting;

namespace Lakona.Game.Server.Health;

public sealed class LakonaGameReadinessEvaluator
{
    private readonly LakonaGameRuntimeOptions _runtime;
    private readonly ClusterOptions _clusterOptions;
    private readonly LakonaHealthReadinessState _readinessState;
    private readonly LakonaGameRuntimeValidator _validator;
    private readonly LakonaServerReadinessState? _serverReadiness;
    private readonly DistributedWorkAdmissionGate? _admissionGate;

    internal const string DistributedAdmissionClosedCode = "LAKONA153";

    public LakonaGameReadinessEvaluator(
        LakonaGameRuntimeOptions runtime,
        ClusterOptions clusterOptions,
        LakonaHealthReadinessState readinessState,
        LakonaGameRuntimeValidator validator)
        : this(
            runtime,
            clusterOptions,
            readinessState,
            validator,
            serverReadiness: null,
            admissionGate: null)
    {
    }

    internal LakonaGameReadinessEvaluator(
        LakonaGameRuntimeOptions runtime,
        ClusterOptions clusterOptions,
        LakonaHealthReadinessState readinessState,
        LakonaGameRuntimeValidator validator,
        LakonaServerReadinessState? serverReadiness,
        DistributedWorkAdmissionGate? admissionGate = null)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _clusterOptions = clusterOptions ?? throw new ArgumentNullException(nameof(clusterOptions));
        _readinessState = readinessState ?? throw new ArgumentNullException(nameof(readinessState));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _serverReadiness = serverReadiness;
        _admissionGate = admissionGate;
    }

    public LakonaGameReadinessSnapshot Evaluate()
    {
        var resolved = LakonaGameReadinessRuntime.ToResolvedRuntimeForValidation(
            _runtime,
            _clusterOptions,
            _readinessState.HotfixAssemblyPath);
        var result = _validator.Validate(resolved);
        var diagnostics = result.Diagnostics
            .Concat(_serverReadiness?.Diagnostics ?? [])
            .Concat(_admissionGate is not null && !_admissionGate.IsOpen
                ?
                [
                    new LakonaGameDiagnostic(
                        DistributedAdmissionClosedCode,
                        LakonaGameDiagnosticSeverity.Error,
                        "Distributed-work admission is closed because this node has no current cluster authority.",
                        "Route application traffic to a node with current quorum authority.")
                ]
                : [])
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
