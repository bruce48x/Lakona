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

    /// <summary>
    /// Registers a named legacy startup plan.
    /// </summary>
    /// <param name="name">The unique startup name.</param>
    /// <param name="createPlan">The callback that creates the startup plan.</param>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty or consists only of white-space characters.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> or <paramref name="createPlan"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">A startup with the same name is already registered.</exception>
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

    /// <summary>
    /// Registers a Startup Actor and a selector that maps each business key to one available replica.
    /// </summary>
    /// <typeparam name="TActor">The Startup Actor type.</typeparam>
    /// <typeparam name="TKey">The business key type used for replica affinity.</typeparam>
    /// <param name="selector">The selector invoked when a key has no existing sticky affinity.</param>
    /// <exception cref="ArgumentNullException"><paramref name="selector"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The actor type already has a Startup registration.</exception>
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

    /// <summary>
    /// Registers a Startup Actor that uses rendezvous hashing for the initial affinity of each business key.
    /// </summary>
    /// <typeparam name="TActor">The Startup Actor type.</typeparam>
    /// <typeparam name="TKey">The business key type used for replica affinity.</typeparam>
    /// <exception cref="InvalidOperationException">The actor type already has a Startup registration.</exception>
    public void RegisterStartup<TActor, TKey>()
    {
        RegisterStartup<TActor, TKey>(ActorPlacementSelectors.StartupRendezvous);
    }

    /// <summary>
    /// Overrides the default rendezvous placement for an Actor with a custom selector.
    /// </summary>
    /// <typeparam name="TActor">The Actor type.</typeparam>
    /// <typeparam name="TKey">The Actor key type.</typeparam>
    /// <param name="selector">The selector that chooses one of the offered host candidates.</param>
    /// <exception cref="ArgumentNullException"><paramref name="selector"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The actor type already has a placement registration.</exception>
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
}
