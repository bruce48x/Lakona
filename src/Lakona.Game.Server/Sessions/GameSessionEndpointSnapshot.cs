namespace Lakona.Game.Server.Sessions;

public sealed class GameSessionEndpointSnapshot
{
    public GameSessionEndpointSnapshot(
        SessionEndpointKey endpoint,
        string connectionId,
        IReadOnlyList<Type> callbackContractTypes)
    {
        Endpoint = endpoint;
        ConnectionId = connectionId ?? throw new ArgumentNullException(nameof(connectionId));
        CallbackContractTypes = callbackContractTypes ?? throw new ArgumentNullException(nameof(callbackContractTypes));
    }

    public SessionEndpointKey Endpoint { get; }

    public string ConnectionId { get; }

    public IReadOnlyList<Type> CallbackContractTypes { get; }
}
