using Server.App.Routing;
using Server.App.Matchmaking;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server;
using Lakona.Game.Server.Hotfix.Abstractions.Timers;
using Microsoft.Extensions.Logging;

namespace Server.App.Matchmaking;

[NodeRole("data")]
public sealed class MatchmakingActor : Actor<MatchmakingQueueId>
{
    internal TimerId MatchmakingTimerId;
    internal string PendingRoomCleanupId = "";
    internal List<MatchmakingQueueTicket> PendingTickets { get; set; } = new();
}
