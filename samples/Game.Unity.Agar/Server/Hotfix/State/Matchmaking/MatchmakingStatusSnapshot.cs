using System.Collections.Generic;
using Agar.Sample.State.Contracts.Matchmaking;

namespace Server.Hotfix.State.Matchmaking;

public sealed class MatchmakingStatusSnapshot
{
    public string QueueId { get; set; } = "";

    public int DefaultRoomSize { get; set; } = 10;

    public int QueuedCount { get; set; }

    public List<MatchmakingQueueTicket> PendingTickets { get; set; } = new();
}