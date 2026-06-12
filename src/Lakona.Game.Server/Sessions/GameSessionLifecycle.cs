using Lakona.Game.Abstractions;

namespace Lakona.Game.Server.Sessions;

public sealed class GameConnectionContext
{
    public GameConnectionContext(string connectionId, string displayName)
    {
        ConnectionId = connectionId ?? throw new ArgumentNullException(nameof(connectionId));
        DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
    }

    public string ConnectionId { get; }

    public string DisplayName { get; }
}

public sealed class GameEndpointBindingContext
{
    public GameEndpointBindingContext(
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

public sealed class GameSessionEndpointBindResult
{
    public GameSessionEndpointBindResult(GameSessionEndpointSnapshot? endpointBecameActive)
    {
        EndpointBecameActive = endpointBecameActive;
    }

    public GameSessionEndpointSnapshot? EndpointBecameActive { get; }
}

public sealed class GameSessionTerminationContext
{
    public GameSessionTerminationContext(SessionTerminationNotice notice)
    {
        Notice = notice ?? throw new ArgumentNullException(nameof(notice));
    }

    public SessionTerminationNotice Notice { get; }
}

public interface IGameSessionLifecycleHandler
{
    ValueTask OnConnectionOpenedAsync(
        GameConnectionContext context,
        CancellationToken cancellationToken = default);

    ValueTask OnEndpointBoundAsync(
        GameEndpointBindingContext context,
        CancellationToken cancellationToken = default);

    ValueTask OnEndpointDisconnectedAsync(
        GameEndpointBindingContext context,
        CancellationToken cancellationToken = default);

    ValueTask OnEndpointExpiredAsync(
        GameEndpointBindingContext context,
        CancellationToken cancellationToken = default);

    ValueTask OnSessionTerminatedAsync(
        GameSessionTerminationContext context,
        CancellationToken cancellationToken = default);
}
