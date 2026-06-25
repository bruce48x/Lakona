using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix.Abstractions;

namespace Lakona.Game.Server.Hotfix;

public class HotfixServiceCall<TRequest> : IHotfixCallContext
{
    public HotfixServiceCall(
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

    public IServiceProvider Services { get; }

    public IActorRuntime Actors { get; }

    public ILakonaGameServer GameServer { get; }
}

public sealed class HotfixServiceCall<TRequest, TCallback> : HotfixServiceCall<TRequest>
    where TCallback : class
{
    public HotfixServiceCall(
        TRequest request,
        string connectionId,
        TCallback callback,
        IServiceProvider services,
        IActorRuntime actors,
        ILakonaGameServer gameServer)
        : base(request, connectionId, services, actors, gameServer)
    {
        Callback = callback ?? throw new ArgumentNullException(nameof(callback));
    }

    public TCallback Callback { get; }
}
