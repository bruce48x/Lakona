using System.Threading.Tasks;
using Lakona.Rpc.Core;

namespace Shared.Interfaces;

[RpcService(RpcContractIds.Services.Battle, NotificationContract = typeof(IBattleNotifications))]
public interface IBattleService
{
    [RpcMethod(RpcContractIds.BattleServiceMethods.JoinAsync)]
    ValueTask<BattleJoinReply> JoinAsync(BattleJoinRequest request);

    [RpcMethod(RpcContractIds.BattleServiceMethods.UpdateInputAsync)]
    ValueTask<CommandReply> UpdateInputAsync(PlayerInputRequest request);
}

[RpcNotificationContract(typeof(IBattleService))]
public interface IBattleNotifications
{
    [RpcNotification(RpcContractIds.BattleNotifications.OnSnapshot)]
    void OnSnapshot(WorldSnapshotReply snapshot);
}
