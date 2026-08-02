using Game.Unity.MMO.Server.App.World;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Sessions;
using Server.App.Generated;
using Shared.Interfaces;

namespace Game.Unity.MMO.Server.Hotfix.World;

[HotfixLifecycle(typeof(IGameSessionLifecycle))]
internal sealed class WorldSessionLifecycle
{
    private readonly ActorAccess _actors;
    public WorldSessionLifecycle(ActorAccess actors) => _actors = actors;

    public ValueTask SessionDisconnectedAsync(HotfixLifecycleCall<GameSessionDisconnectedRequest> call) => default;

    public ValueTask SessionExpiredAsync(HotfixLifecycleCall<GameSessionExpiredRequest> call) => _actors
        .Startup<ZoneActor>(new ZoneId(WorldProtocol.DefaultZoneId))
        .CallAsync(static behavior => behavior.LeaveAsync,
            new ZoneLeaveRequest
            {
                CharacterId = call.Request.OwnerKey,
                Session = new GameSessionKey(call.Request.OwnerKey, call.Request.SessionId)
            }, CancellationToken.None);
}
