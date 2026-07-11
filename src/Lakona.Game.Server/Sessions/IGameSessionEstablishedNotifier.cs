using Lakona.Game.Abstractions.Sessions;

namespace Lakona.Game.Server.Sessions;

internal interface IGameSessionEstablishedNotifier
{
    ValueTask NotifyAsync(
        string connectionId,
        GameSessionEstablished established,
        CancellationToken cancellationToken = default);
}

internal sealed class GameSessionEstablishedNotifier(
    GameFrameworkConnectionRegistry connections) : IGameSessionEstablishedNotifier
{
    public ValueTask NotifyAsync(
        string connectionId,
        GameSessionEstablished established,
        CancellationToken cancellationToken = default)
    {
        var connection = connections.Get(connectionId);
        if (connection is null)
        {
            return default;
        }
        return connection.SendRawNotificationAsync(
            GameSessionNotificationRpcIds.ServiceId,
            GameSessionNotificationRpcIds.EstablishedNotificationId,
            Lakona.Game.Abstractions.LakonaInternalCodec.EncodeGameSessionEstablished(established),
            cancellationToken);
    }
}

internal sealed class NoopGameSessionEstablishedNotifier : IGameSessionEstablishedNotifier
{
    public ValueTask NotifyAsync(
        string connectionId,
        GameSessionEstablished established,
        CancellationToken cancellationToken = default) => default;
}
