using Agar.Sample.State.Contracts.Matchmaking;
using Lakona.Game.Server.Actors;
using Server.App.Services;

namespace Agar.Sample.State.Matchmaking;

public sealed class MatchmakingActor : Actor
{
    internal const int DefaultRoomSize = 10;

    internal readonly IPlayerSessionStateStore Sessions;
    internal readonly IRoomStateStore Rooms;
    internal readonly BattleRuntimeGatewayResolver RuntimeGateways;
    internal bool RecordExists;
    internal MatchmakingState State = new();

    public MatchmakingActor(
        IPlayerSessionStateStore sessions,
        IRoomStateStore rooms,
        BattleRuntimeGatewayResolver runtimeGateways)
    {
        Sessions = sessions;
        Rooms = rooms;
        RuntimeGateways = runtimeGateways;
    }
}
