namespace Lakona.Game.Server.Hotfix.Abstractions;

public sealed class ActorStartCall
{
    public ActorStartCall(
        object actorId,
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actorId);

        ActorId = actorId;
        Services = services ?? throw new ArgumentNullException(nameof(services));
        CancellationToken = cancellationToken;
    }

    public object ActorId { get; }

    public IServiceProvider Services { get; }

    public CancellationToken CancellationToken { get; }
}
