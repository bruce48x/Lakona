namespace Lakona.Game.Server.Hotfix.Abstractions;

public sealed class ActorStartupPlan
{
    public static ActorStartupPlan Empty { get; } = new([]);

    public ActorStartupPlan(IReadOnlyList<ActorStartupInstance> actors)
    {
        Actors = actors?.ToArray() ?? throw new ArgumentNullException(nameof(actors));
    }

    public IReadOnlyList<ActorStartupInstance> Actors { get; }

    public static ActorStartupPlan Create<TActor>(object actorId)
    {
        ArgumentNullException.ThrowIfNull(actorId);
        return new ActorStartupPlan([new ActorStartupInstance(typeof(TActor), actorId)]);
    }

    public static ActorStartupPlan CreateMany<TActor, TActorId>(IEnumerable<TActorId> actorIds)
    {
        ArgumentNullException.ThrowIfNull(actorIds);
        return new ActorStartupPlan(
            actorIds.Select(static actorId =>
            {
                ArgumentNullException.ThrowIfNull(actorId);
                return new ActorStartupInstance(typeof(TActor), actorId);
            }).ToArray());
    }
}
