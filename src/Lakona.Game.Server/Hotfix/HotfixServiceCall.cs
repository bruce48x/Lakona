using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Sessions;

namespace Lakona.Game.Server.Hotfix;

public readonly struct HotfixServiceCall<TRequest> : IHotfixServiceCall<TRequest>
{
    public HotfixServiceCall(
        TRequest request,
        string connectionId,
        IServiceProvider services,
        IActorRuntime actors,
        ILakonaGameServer gameServer)
        : this(request, connectionId, currentSession: null, services, actors, gameServer)
    {
    }

    public HotfixServiceCall(
        TRequest request,
        string connectionId,
        GameSessionKey? currentSession,
        IServiceProvider services,
        IActorRuntime actors,
        ILakonaGameServer gameServer)
        : this(request, connectionId, currentSession, GameSessionItems.Empty, services, actors, gameServer)
    {
    }

    public HotfixServiceCall(
        TRequest request,
        string connectionId,
        GameSessionKey? currentSession,
        GameSessionItems currentSessionItems,
        IServiceProvider services,
        IActorRuntime actors,
        ILakonaGameServer gameServer)
    {
        Request = request;
        ConnectionId = connectionId ?? throw new ArgumentNullException(nameof(connectionId));
        CurrentSession = currentSession;
        CurrentSessionItems = currentSessionItems ?? throw new ArgumentNullException(nameof(currentSessionItems));
        Services = services ?? throw new ArgumentNullException(nameof(services));
        Actors = actors ?? throw new ArgumentNullException(nameof(actors));
        GameServer = gameServer ?? throw new ArgumentNullException(nameof(gameServer));
    }

    public TRequest Request { get; }

    public string ConnectionId { get; }

    public GameSessionKey? CurrentSession { get; }

    public GameSessionItems CurrentSessionItems { get; }

    public IServiceProvider Services { get; }

    public IActorRuntime Actors { get; }

    public ILakonaGameServer GameServer { get; }
}

public readonly struct HotfixServiceCall<TRequest, TCallback> : IHotfixServiceCall<TRequest>
    where TCallback : class
{
    public HotfixServiceCall(
        TRequest request,
        string connectionId,
        TCallback callback,
        IServiceProvider services,
        IActorRuntime actors,
        ILakonaGameServer gameServer)
        : this(request, connectionId, callback, currentSession: null, services, actors, gameServer)
    {
    }

    public HotfixServiceCall(
        TRequest request,
        string connectionId,
        TCallback callback,
        GameSessionKey? currentSession,
        IServiceProvider services,
        IActorRuntime actors,
        ILakonaGameServer gameServer)
        : this(request, connectionId, callback, currentSession, GameSessionItems.Empty, services, actors, gameServer)
    {
    }

    public HotfixServiceCall(
        TRequest request,
        string connectionId,
        TCallback callback,
        GameSessionKey? currentSession,
        GameSessionItems currentSessionItems,
        IServiceProvider services,
        IActorRuntime actors,
        ILakonaGameServer gameServer)
    {
        Request = request;
        ConnectionId = connectionId ?? throw new ArgumentNullException(nameof(connectionId));
        Callback = callback ?? throw new ArgumentNullException(nameof(callback));
        CurrentSession = currentSession;
        CurrentSessionItems = currentSessionItems ?? throw new ArgumentNullException(nameof(currentSessionItems));
        Services = services ?? throw new ArgumentNullException(nameof(services));
        Actors = actors ?? throw new ArgumentNullException(nameof(actors));
        GameServer = gameServer ?? throw new ArgumentNullException(nameof(gameServer));
    }

    public TRequest Request { get; }

    public string ConnectionId { get; }

    public TCallback Callback { get; }

    public GameSessionKey? CurrentSession { get; }

    public GameSessionItems CurrentSessionItems { get; }

    public IServiceProvider Services { get; }

    public IActorRuntime Actors { get; }

    public ILakonaGameServer GameServer { get; }
}
