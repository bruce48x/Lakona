using Agar.Sample.State.Contracts.Matchmaking;
using Lakona.Game.Server.Actors;

namespace Agar.Sample.State.Matchmaking;

public sealed class MatchmakingActor : Actor
{
    internal const int DefaultRoomSize = 10;

    internal bool RecordExists;
    internal MatchmakingState State = new();
}
