namespace Lakona.Game.Server.Hotfix.Abstractions;

public sealed record ActorPlacementDeclaration(
    Type ActorType,
    Type KeyType,
    Delegate Selector)
{
    public static ActorPlacementDeclaration Create<TActor, TKey>(
        Func<ActorPlacementContext<TKey>, ActorHostCandidate> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        return new ActorPlacementDeclaration(typeof(TActor), typeof(TKey), selector);
    }
}
