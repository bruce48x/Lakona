namespace Lakona.Game.Server.Hotfix.Abstractions;

public sealed class ActorHostBuilder
{
    private readonly List<ActorStartupDeclaration> _startups = [];
    private readonly List<ActorPlacementDeclaration> _placements = [];
    private readonly HashSet<string> _startupNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<Type> _startupActors = [];
    private readonly HashSet<Type> _placementActors = [];

    public IReadOnlyList<ActorStartupDeclaration> Startups => _startups.ToArray();

    public IReadOnlyList<ActorPlacementDeclaration> Placements => _placements.ToArray();

    public void RegisterStartup(
        string name,
        Func<ActorStartupContext, ActorStartupPlan> createPlan)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(createPlan);

        if (!_startupNames.Add(name))
        {
            throw new InvalidOperationException($"Actor startup '{name}' is already registered.");
        }

        _startups.Add(new ActorStartupDeclaration(name, createPlan));
    }

    public void RegisterStartup<TActor, TKey>(
        Func<StartupActorSelectionContext<TKey>, StartupActorCandidate> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);

        if (!_startupActors.Add(typeof(TActor)))
        {
            throw new InvalidOperationException(
                $"Actor startup for '{typeof(TActor).FullName}' is already registered.");
        }

        _startups.Add(ActorStartupDeclaration.Create<TActor, TKey>(selector));
    }

    public void RegisterPlacement<TActor, TKey>(
        Func<ActorPlacementContext<TKey>, ActorHostCandidate> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);

        if (!_placementActors.Add(typeof(TActor)))
        {
            throw new InvalidOperationException(
                $"Actor placement for '{typeof(TActor).FullName}' is already registered.");
        }

        _placements.Add(ActorPlacementDeclaration.Create<TActor, TKey>(selector));
    }

    public void RegisterPlacement<TActor, TKey>()
    {
        RegisterPlacement<TActor, TKey>(ActorPlacementSelectors.Rendezvous);
    }
}
