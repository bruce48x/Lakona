namespace Lakona.Game.Server.Sessions;

/// <summary>
/// Represents a bound game session endpoint and its typed client callback proxy.
/// </summary>
/// <typeparam name="TCallback">The callback contract type exposed by the bound client endpoint.</typeparam>
public sealed class GameSessionEndpointBinding<TCallback>
    where TCallback : class
{
    /// <summary>
    /// Initializes a new endpoint binding.
    /// </summary>
    /// <param name="endpoint">The session endpoint represented by the binding.</param>
    /// <param name="connectionId">The RPC connection currently associated with the endpoint.</param>
    /// <param name="callback">The typed callback proxy for sending messages to the client endpoint.</param>
    public GameSessionEndpointBinding(
        SessionEndpointKey endpoint,
        string connectionId,
        TCallback callback)
    {
        Endpoint = endpoint;
        ConnectionId = connectionId;
        Callback = callback;
    }

    /// <summary>
    /// Gets the session endpoint represented by the binding.
    /// </summary>
    public SessionEndpointKey Endpoint { get; }

    /// <summary>
    /// Gets the RPC connection currently associated with the endpoint.
    /// </summary>
    public string ConnectionId { get; }

    /// <summary>
    /// Gets the typed callback proxy for sending messages to the client endpoint.
    /// </summary>
    public TCallback Callback { get; }
}
