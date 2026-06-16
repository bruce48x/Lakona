namespace Lakona.Game.Server.Sessions;

public interface IClientSessionRouteRegistrar
{
    ValueTask RegisterAsync(
        GameSessionKey session,
        CancellationToken cancellationToken = default);

    ValueTask RemoveAsync(
        GameSessionKey session,
        CancellationToken cancellationToken = default);
}
