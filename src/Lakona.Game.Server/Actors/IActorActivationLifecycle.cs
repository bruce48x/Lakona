namespace Lakona.Game.Server.Actors;

internal interface IActorActivationLifecycle
{
    ValueTask DrainAsync(CancellationToken cancellationToken = default);
}
