using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Hotfix.Abstractions.Timers;
using Microsoft.Extensions.DependencyInjection;

namespace Lakona.Game.Server.Hotfix;

internal sealed class HotfixFeatureLifecycleCoordinator
{
    private readonly HotfixFeatureLifecycleInvoker invoker;

    public HotfixFeatureLifecycleCoordinator()
        : this(new HotfixFeatureLifecycleInvoker())
    {
    }

    public HotfixFeatureLifecycleCoordinator(HotfixFeatureLifecycleInvoker invoker)
    {
        this.invoker = invoker ?? throw new ArgumentNullException(nameof(invoker));
    }

    public async ValueTask<HotfixFeatureLifecycleSnapshot> StartCandidateAsync(
        HotfixFeatureLifecycleSnapshot previous,
        HotfixRuntimeSnapshot candidateRuntime,
        IReadOnlyList<HotfixFeatureDeclaration> candidateFeatures,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(candidateRuntime);
        ArgumentNullException.ThrowIfNull(candidateFeatures);
        cancellationToken.ThrowIfCancellationRequested();

        var states = new Dictionary<string, HotfixFeatureState>(StringComparer.OrdinalIgnoreCase);
        foreach (var feature in candidateFeatures)
        {
            states.Add(feature.Name, previous.States.TryGetValue(feature.Name, out var existing)
                ? existing
                : new HotfixFeatureState());
        }

        var started = new List<HotfixFeatureDeclaration>();
        var rootTimerBackend = candidateRuntime.Services.GetService<ILakonaTimerBackend>();
        var stagingTimerBackend = rootTimerBackend?.CreateStagingBackend();
        using var lease = candidateRuntime.AcquireLease();
        try
        {
            foreach (var feature in candidateFeatures)
            {
                if (previous.States.ContainsKey(feature.Name))
                {
                    continue;
                }

                await invoker.StartAsync(
                    feature,
                    states[feature.Name],
                    candidateRuntime.Services,
                    stagingTimerBackend,
                    cancellationToken).ConfigureAwait(false);
                started.Add(feature);
            }

            return new HotfixFeatureLifecycleSnapshot(
                candidateRuntime,
                candidateFeatures,
                states,
                rootTimerBackend,
                stagingTimerBackend);
        }
        catch
        {
            for (var index = started.Count - 1; index >= 0; index--)
            {
                var feature = started[index];
                await invoker.StopAsync(
                    feature,
                    states[feature.Name],
                    candidateRuntime.Services,
                    stagingTimerBackend,
                    CancellationToken.None).ConfigureAwait(false);
            }

            if (rootTimerBackend is not null && stagingTimerBackend is not null)
            {
                await rootTimerBackend.RollbackStagedTimersAsync(stagingTimerBackend, CancellationToken.None)
                    .ConfigureAwait(false);
            }

            throw;
        }
    }

    public async ValueTask StopRemovedAsync(
        HotfixFeatureLifecycleSnapshot previous,
        HotfixFeatureLifecycleSnapshot next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(next);
        cancellationToken.ThrowIfCancellationRequested();

        if (previous.Runtime is null)
        {
            return;
        }

        using var lease = previous.Runtime.AcquireLease();
        var timerBackend = previous.Runtime.Services.GetService<ILakonaTimerBackend>();
        var removed = previous.Features
            .Where(feature => !next.States.ContainsKey(feature.Name))
            .Reverse()
            .ToArray();
        foreach (var feature in removed)
        {
            if (!previous.States.TryGetValue(feature.Name, out var state))
            {
                continue;
            }

            await invoker.StopAsync(
                feature,
                state,
                previous.Runtime.Services,
                timerBackend,
                cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask CommitCandidateTimersAsync(
        HotfixFeatureLifecycleSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        cancellationToken.ThrowIfCancellationRequested();
        if (snapshot.RootTimerBackend is null || snapshot.StagingTimerBackend is null)
        {
            return;
        }

        await snapshot.RootTimerBackend.CommitStagedTimersAsync(snapshot.StagingTimerBackend, cancellationToken)
            .ConfigureAwait(false);
    }
}

internal sealed class HotfixFeatureLifecycleSnapshot
{
    public static HotfixFeatureLifecycleSnapshot Empty { get; } = new(
        runtime: null,
        Array.Empty<HotfixFeatureDeclaration>(),
        new Dictionary<string, HotfixFeatureState>(StringComparer.OrdinalIgnoreCase));

    public HotfixFeatureLifecycleSnapshot(
        HotfixRuntimeSnapshot? runtime,
        IReadOnlyList<HotfixFeatureDeclaration> features,
        IReadOnlyDictionary<string, HotfixFeatureState> states,
        ILakonaTimerBackend? rootTimerBackend = null,
        ILakonaTimerBackend? stagingTimerBackend = null)
    {
        Runtime = runtime;
        Features = features ?? throw new ArgumentNullException(nameof(features));
        States = states ?? throw new ArgumentNullException(nameof(states));
        RootTimerBackend = rootTimerBackend;
        StagingTimerBackend = stagingTimerBackend;
    }

    public HotfixRuntimeSnapshot? Runtime { get; }

    public IReadOnlyList<HotfixFeatureDeclaration> Features { get; }

    public IReadOnlyDictionary<string, HotfixFeatureState> States { get; }

    internal ILakonaTimerBackend? RootTimerBackend { get; }

    internal ILakonaTimerBackend? StagingTimerBackend { get; }

    public IReadOnlyList<string> FeatureNames => Features.Select(static feature => feature.Name).ToArray();

    public HotfixFeatureLifecycleSnapshot WithStartedFeature(
        HotfixFeatureDeclaration feature,
        HotfixFeatureState state,
        HotfixRuntimeSnapshot runtime)
    {
        ArgumentNullException.ThrowIfNull(feature);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(runtime);

        var features = Features.Append(feature).ToArray();
        var states = new Dictionary<string, HotfixFeatureState>(States, StringComparer.OrdinalIgnoreCase)
        {
            [feature.Name] = state
        };
        return new HotfixFeatureLifecycleSnapshot(runtime, features, states);
    }
}
