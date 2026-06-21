using Agar.Sample.State.Contracts.Matchmaking;
using Lakona.Rpc.Core;
using Shared.Gameplay;

namespace Server.App.Hotfix;

public interface IAgarRuntimeService
{
    [RpcMethod(AgarRuntimeMethodIds.TickMatchmaking)]
    ValueTask TickMatchmakingAsync(AgarMatchmakingTickRequest request);

    [RpcMethod(AgarRuntimeMethodIds.CommitRoomSettlement)]
    ValueTask CommitRoomSettlementAsync(AgarRoomSettlementRequest request);
}

public static class AgarRuntimeMethodIds
{
    public const int TickMatchmaking = 1;
    public const int CommitRoomSettlement = 4;
}

public sealed class AgarMatchmakingTickRequest
{
    public DateTime ObservedAtUtc { get; set; }
}

public sealed class AgarRoomSettlementRequest
{
    public string RoomId { get; set; } = "";
    public string SettlementId { get; set; } = "";
    public DateTime FinishedAtUtc { get; set; }
    public int Tick { get; set; }
    public MatchSettlementResult Settlement { get; set; } = new();
}
