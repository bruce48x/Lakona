namespace Lakona.Game.Server.Sessions;

/// <summary>
/// Captures the current framework-owned binding state for a game session endpoint.
/// </summary>
public sealed class GameSessionEndpointSnapshot
{
    /// <summary>
    /// Initializes a new endpoint snapshot.
    /// </summary>
    /// <param name="endpoint">The session endpoint represented by the snapshot.</param>
    /// <param name="connectionId">The RPC connection currently associated with the endpoint.</param>
    /// <param name="callbackContractTypes">The callback contracts exposed by the bound client endpoint.</param>
    public GameSessionEndpointSnapshot(
        SessionEndpointKey endpoint,
        string connectionId,
        IReadOnlyList<Type> callbackContractTypes)
    {
        Endpoint = endpoint;
        ConnectionId = connectionId ?? throw new ArgumentNullException(nameof(connectionId));
        CallbackContractTypes = callbackContractTypes ?? throw new ArgumentNullException(nameof(callbackContractTypes));
    }

    /// <summary>
    /// Gets the session endpoint represented by the snapshot.
    /// </summary>
    public SessionEndpointKey Endpoint { get; }

    /// <summary>
    /// Gets the RPC connection currently associated with the endpoint.
    /// </summary>
    public string ConnectionId { get; }

    /// <summary>
    /// Gets the callback contracts exposed by the bound client endpoint.
    /// </summary>
    public IReadOnlyList<Type> CallbackContractTypes { get; }
}
