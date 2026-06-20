using Agar.Sample.State.Contracts.Matchmaking;
using Lakona.Rpc.Core;
using Shared.Gameplay;

namespace Server.App.Hotfix;

public interface IAgarRuntimeService
{
    [RpcMethod(AgarRuntimeMethodIds.TickMatchmaking)]
    ValueTask TickMatchmakingAsync(AgarMatchmakingTickRequest request);

    [RpcMethod(AgarRuntimeMethodIds.MarkControlDisconnected)]
    ValueTask MarkControlDisconnectedAsync(AgarPlayerDisconnectRequest request);

    [RpcMethod(AgarRuntimeMethodIds.CleanupExpiredSession)]
    ValueTask CleanupExpiredSessionAsync(AgarPlayerDisconnectRequest request);

    [RpcMethod(AgarRuntimeMethodIds.CommitRoomSettlement)]
    ValueTask CommitRoomSettlementAsync(AgarRoomSettlementRequest request);
}

public static class AgarRuntimeMethodIds
{
    public const int TickMatchmaking = 1;
    public const int MarkControlDisconnected = 2;
    public const int CleanupExpiredSession = 3;
    public const int CommitRoomSettlement = 4;
}

public sealed class AgarMatchmakingTickRequest
{
    public DateTime ObservedAtUtc { get; set; }
}

public sealed class AgarPlayerDisconnectRequest
{
    public string PlayerId { get; set; } = "";
    public string ConnectionId { get; set; } = "";
    public DateTime DisconnectedAtUtc { get; set; }
    public string Reason { get; set; } = "";
}

public sealed class AgarRoomSettlementRequest
{
    public string RoomId { get; set; } = "";
    public string SettlementId { get; set; } = "";
    public DateTime FinishedAtUtc { get; set; }
    public int Tick { get; set; }
    public MatchSettlementResult Settlement { get; set; } = new();
}
