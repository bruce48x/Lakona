namespace Lakona.Game.Server.Hotfix.Abstractions;

public sealed record ActorStartupDeclaration
{
    private ActorStartupDeclaration(Type actorType, Type keyType, Delegate selector)
    {
        ActorType = actorType;
        KeyType = keyType;
        Selector = selector;
    }

    public Type ActorType { get; }

    public Type KeyType { get; }

    public Delegate Selector { get; }

    public static ActorStartupDeclaration Create<TActor, TKey>(
        Func<StartupActorSelectionContext<TKey>, StartupActorCandidate> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        return new ActorStartupDeclaration(typeof(TActor), typeof(TKey), selector);
    }
}
