using Lakona.Game.Server.Actors;

namespace Lakona.Game.Server.Hotfix;

public sealed class HotfixLifecycleCall<TRequest> : HotfixServiceCall<TRequest>
{
    public HotfixLifecycleCall(
        TRequest request,
        string connectionId,
        IServiceProvider services,
        IActorRuntime actors,
        ILakonaGameServer gameServer)
        : base(request, connectionId, services, actors, gameServer)
    {
    }
}
