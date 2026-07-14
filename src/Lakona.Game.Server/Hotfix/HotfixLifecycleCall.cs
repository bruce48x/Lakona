using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Sessions;

namespace Lakona.Game.Server.Hotfix;

public readonly struct HotfixLifecycleCall<TRequest> : IHotfixCallContext
{
    public HotfixLifecycleCall(
        TRequest request,
        string connectionId,
        IServiceProvider services,
        IActorRuntime actors,
        ILakonaGameServer gameServer)
    {
        Request = request;
        ConnectionId = connectionId ?? throw new ArgumentNullException(nameof(connectionId));
        Services = services ?? throw new ArgumentNullException(nameof(services));
        Actors = actors ?? throw new ArgumentNullException(nameof(actors));
        GameServer = gameServer ?? throw new ArgumentNullException(nameof(gameServer));
    }

    public TRequest Request { get; }

    public string ConnectionId { get; }

    public GameSessionKey? CurrentSession => null;

    public GameSessionItems CurrentSessionItems => GameSessionItems.Empty;

    public IServiceProvider Services { get; }

    public IActorRuntime Actors { get; }

    public ILakonaGameServer GameServer { get; }
}
