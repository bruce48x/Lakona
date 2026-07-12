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
    GameFrameworkConnectionRegistry connections,
    GameSessionEstablishedAcknowledgements acknowledgements) : IGameSessionEstablishedNotifier
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
        return NotifyAndWaitAsync(connection, established, cancellationToken);
    }

    private async ValueTask NotifyAndWaitAsync(
        Lakona.Rpc.Server.RpcSession connection,
        GameSessionEstablished established,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        var acknowledgement = acknowledgements.WaitAsync(connection.ContextId, timeout.Token);
        try
        {
            await connection.SendRawNotificationAsync(
                GameSessionNotificationRpcIds.ServiceId,
                GameSessionNotificationRpcIds.EstablishedNotificationId,
                Lakona.Game.Abstractions.LakonaInternalCodec.EncodeGameSessionEstablished(established),
                timeout.Token).ConfigureAwait(false);
            await acknowledgement.ConfigureAwait(false);
        }
        finally
        {
            acknowledgements.Cancel(connection.ContextId);
        }
    }
}

internal sealed class NoopGameSessionEstablishedNotifier : IGameSessionEstablishedNotifier
{
    public ValueTask NotifyAsync(
        string connectionId,
        GameSessionEstablished established,
        CancellationToken cancellationToken = default) => default;
}
