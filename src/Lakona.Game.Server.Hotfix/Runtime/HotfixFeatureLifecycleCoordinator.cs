using System.Runtime.ExceptionServices;
using System.Runtime.Loader;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Hotfix.Abstractions.Timers;
using Lakona.Game.Server.Hotfix.Dispatch;
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
        var rollbackScopes = new List<HotfixCandidateRollbackScope>();
        using var lease = candidateRuntime.AcquireLease();
        using var dispatchTimerScope = stagingTimerBackend is null
            ? null
            : HotfixDispatchRuntimeScope.Enter(lease, stagingTimerBackend);
        try
        {
            foreach (var feature in candidateFeatures)
            {
                if (previous.States.ContainsKey(feature.Name))
                {
                    continue;
                }

                var rollbackScope = await HotfixCandidateRollbackScope
                    .BeginAsync(feature.Name, candidateRuntime.Services, cancellationToken)
                    .ConfigureAwait(false);
                try
                {
                    await invoker.StartAsync(
                        feature,
                        states[feature.Name],
                        candidateRuntime.Services,
                        stagingTimerBackend,
                        cancellationToken).ConfigureAwait(false);
                    started.Add(feature);
                    ValidateFeatureState(feature, states[feature.Name]);
                    await rollbackScope.CommitAsync(cancellationToken).ConfigureAwait(false);
                    rollbackScopes.Add(rollbackScope);
                }
                catch
                {
                    await rollbackScope.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                    await rollbackScope.DisposeAsync().ConfigureAwait(false);
                    throw;
                }
            }

            foreach (var feature in candidateFeatures)
            {
                ValidateFeatureState(feature, states[feature.Name]);
            }

            return new HotfixFeatureLifecycleSnapshot(
                candidateRuntime,
                candidateFeatures,
                states,
                rootTimerBackend,
                stagingTimerBackend,
                started.ToArray(),
                rollbackScopes.ToArray());
        }
        catch (Exception ex)
        {
            try
            {
                await StopStartedFeaturesSuppressingAsync(
                    started,
                    states,
                    candidateRuntime.Services,
                    stagingTimerBackend).ConfigureAwait(false);
                await RollbackScopesSuppressingAsync(rollbackScopes).ConfigureAwait(false);
            }
            finally
            {
                if (rootTimerBackend is not null && stagingTimerBackend is not null)
                {
                    await rootTimerBackend.RollbackStagedTimersAsync(stagingTimerBackend, CancellationToken.None)
                        .ConfigureAwait(false);
                }
            }

            ExceptionDispatchInfo.Capture(ex).Throw();
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

            try
            {
                await invoker.StopAsync(
                    feature,
                    state,
                    previous.Runtime.Services,
                    timerBackend,
                    cancellationToken).ConfigureAwait(false);
            }
            catch
            {
            }
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

    public async ValueTask RollbackCandidateAsync(
        HotfixFeatureLifecycleSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        cancellationToken.ThrowIfCancellationRequested();

        if (snapshot.Runtime is not null)
        {
            using var lease = snapshot.Runtime.AcquireLease();
            try
            {
                await StopStartedFeaturesSuppressingAsync(
                    snapshot.StartedFeatures,
                    snapshot.States,
                    snapshot.Runtime.Services,
                    snapshot.StagingTimerBackend).ConfigureAwait(false);
                await RollbackScopesSuppressingAsync(snapshot.RollbackScopes).ConfigureAwait(false);
            }
            finally
            {
                if (snapshot.RootTimerBackend is not null && snapshot.StagingTimerBackend is not null)
                {
                    await snapshot.RootTimerBackend.RollbackStagedTimersAsync(snapshot.StagingTimerBackend, CancellationToken.None)
                        .ConfigureAwait(false);
                }
            }
            return;
        }

        if (snapshot.RootTimerBackend is not null && snapshot.StagingTimerBackend is not null)
        {
            await snapshot.RootTimerBackend.RollbackStagedTimersAsync(snapshot.StagingTimerBackend, CancellationToken.None)
                .ConfigureAwait(false);
        }
    }

    private static async ValueTask RollbackScopesSuppressingAsync(
        IReadOnlyList<HotfixCandidateRollbackScope> scopes)
    {
        for (var index = scopes.Count - 1; index >= 0; index--)
        {
            try
            {
                await scopes[index].RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
            }
            finally
            {
                await scopes[index].DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private async ValueTask StopStartedFeaturesSuppressingAsync(
        IReadOnlyList<HotfixFeatureDeclaration> started,
        IReadOnlyDictionary<string, HotfixFeatureState> states,
        IServiceProvider services,
        ILakonaTimerBackend? timerBackend)
    {
        for (var index = started.Count - 1; index >= 0; index--)
        {
            var feature = started[index];
            if (!states.TryGetValue(feature.Name, out var state))
            {
                continue;
            }

            try
            {
                await invoker.StopAsync(
                    feature,
                    state,
                    services,
                    timerBackend,
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
            }
        }
    }

    private static void ValidateFeatureState(
        HotfixFeatureDeclaration feature,
        HotfixFeatureState state)
    {
        foreach (var item in state.Items)
        {
            var value = item.Value;
            if (value is null)
            {
                continue;
            }

            var loadContext = AssemblyLoadContext.GetLoadContext(value.GetType().Assembly);
            if (loadContext?.IsCollectible == true)
            {
                throw new InvalidOperationException(
                    $"HotfixFeatureState item '{item.Key}' for feature '{feature.Name}' contains value type '{value.GetType().FullName}' from a collectible hotfix AssemblyLoadContext. Feature state values must be reload-safe values from the default or shared non-collectible AssemblyLoadContext.");
            }
        }
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
        ILakonaTimerBackend? stagingTimerBackend = null,
        IReadOnlyList<HotfixFeatureDeclaration>? startedFeatures = null,
        IReadOnlyList<HotfixCandidateRollbackScope>? rollbackScopes = null)
    {
        Runtime = runtime;
        Features = features ?? throw new ArgumentNullException(nameof(features));
        States = states ?? throw new ArgumentNullException(nameof(states));
        RootTimerBackend = rootTimerBackend;
        StagingTimerBackend = stagingTimerBackend;
        StartedFeatures = startedFeatures ?? Array.Empty<HotfixFeatureDeclaration>();
        RollbackScopes = rollbackScopes ?? Array.Empty<HotfixCandidateRollbackScope>();
    }

    public HotfixRuntimeSnapshot? Runtime { get; }

    public IReadOnlyList<HotfixFeatureDeclaration> Features { get; }

    public IReadOnlyDictionary<string, HotfixFeatureState> States { get; }

    internal ILakonaTimerBackend? RootTimerBackend { get; }

    internal ILakonaTimerBackend? StagingTimerBackend { get; }

    internal IReadOnlyList<HotfixFeatureDeclaration> StartedFeatures { get; }

    internal IReadOnlyList<HotfixCandidateRollbackScope> RollbackScopes { get; }

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
