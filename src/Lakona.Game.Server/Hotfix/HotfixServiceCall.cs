using Lakona.Game.Abstractions;
using Lakona.Game.Server.Actors;

namespace Lakona.Game.Server.Hotfix;

public class HotfixServiceCall<TRequest>
{
    public HotfixServiceCall(
        TRequest request,
        string connectionId,
        GameEndpointName endpointName,
        IServiceProvider services,
        IActorRuntime actors,
        ILakonaGameServer gameServer)
    {
        Request = request;
        ConnectionId = connectionId ?? throw new ArgumentNullException(nameof(connectionId));
        EndpointName = endpointName;
        Services = services ?? throw new ArgumentNullException(nameof(services));
        Actors = actors ?? throw new ArgumentNullException(nameof(actors));
        GameServer = gameServer ?? throw new ArgumentNullException(nameof(gameServer));
    }

    public TRequest Request { get; }

    public string ConnectionId { get; }

    public GameEndpointName EndpointName { get; }

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
        GameEndpointName endpointName,
        TCallback callback,
        IServiceProvider services,
        IActorRuntime actors,
        ILakonaGameServer gameServer)
        : base(request, connectionId, endpointName, services, actors, gameServer)
    {
        Callback = callback ?? throw new ArgumentNullException(nameof(callback));
    }

    public TCallback Callback { get; }
}
