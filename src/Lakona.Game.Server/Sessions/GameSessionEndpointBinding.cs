namespace Lakona.Game.Server.Sessions;

public sealed class GameSessionEndpointBinding<TCallback>
    where TCallback : class
{
    public GameSessionEndpointBinding(
        SessionEndpointKey endpoint,
        string connectionId,
        TCallback callback)
    {
        Endpoint = endpoint;
        ConnectionId = connectionId;
        Callback = callback;
    }

    public SessionEndpointKey Endpoint { get; }

    public string ConnectionId { get; }

    public TCallback Callback { get; }
}
