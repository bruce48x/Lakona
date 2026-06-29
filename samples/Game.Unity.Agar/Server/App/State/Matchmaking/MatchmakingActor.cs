using Agar.Sample.State.Contracts;
using Agar.Sample.State.Contracts.Matchmaking;
using Lakona.Game.Server.Actors;
using Microsoft.Extensions.Logging;

namespace Agar.Sample.State.Matchmaking;

public sealed class MatchmakingActor : Actor<MatchmakingQueueId>
{
    internal const int DefaultRoomSize = 10;

    internal bool RecordExists;
    internal MatchmakingState State = new();
}
