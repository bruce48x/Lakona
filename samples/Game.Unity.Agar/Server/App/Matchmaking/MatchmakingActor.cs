using Server.App.Routing;
using Server.App.Matchmaking;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix.Abstractions.Timers;
using Microsoft.Extensions.Logging;

namespace Server.App.Matchmaking;

public sealed class MatchmakingActor : Actor<MatchmakingQueueId>
{
    internal const int DefaultRoomSize = 10;

    internal bool RecordExists;
    internal TimerId MatchmakingTimerId;
    internal MatchmakingState State = new();
}
