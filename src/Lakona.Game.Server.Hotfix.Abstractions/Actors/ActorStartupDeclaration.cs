namespace Lakona.Game.Server.Hotfix.Abstractions;

public sealed record ActorStartupDeclaration
{
    public ActorStartupDeclaration(
        string name,
        Func<ActorStartupContext, ActorStartupPlan> createPlan)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(createPlan);
        Name = name;
        CreatePlan = createPlan;
    }

    private ActorStartupDeclaration(Type actorType, Type keyType, Delegate selector)
    {
        ActorType = actorType;
        KeyType = keyType;
        Selector = selector;
    }

    public Type? ActorType { get; }

    public Type? KeyType { get; }

    public Delegate? Selector { get; }

    public string? Name { get; }

    public Func<ActorStartupContext, ActorStartupPlan>? CreatePlan { get; }

    public bool IsLegacy => CreatePlan is not null;

    public static ActorStartupDeclaration Create<TActor, TKey>(
        Func<StartupActorSelectionContext<TKey>, StartupActorCandidate> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        return new ActorStartupDeclaration(typeof(TActor), typeof(TKey), selector);
    }
}
