using System.Threading.Tasks;
using Lakona.Rpc.Core;

namespace Game.Rpc.Contracts
{
    [RpcService(RpcContractIds.Services.Player, NotificationContract = typeof(IPlayerNotifications))]
    public interface IPlayerService
    {
        [RpcMethod(RpcContractIds.PlayerServiceMethods.LoginAsync)]
        ValueTask<LoginReply> LoginAsync(LoginRequest req);

        [RpcMethod(RpcContractIds.PlayerServiceMethods.IncrStep)]
        ValueTask<StepReply> IncrStep(StepRequest req);
    }

    [RpcNotificationContract(typeof(IPlayerService))]
    public interface IPlayerNotifications
    {
        [RpcNotification(RpcContractIds.PlayerNotifications.OnPlayerNotify)]
        void OnPlayerNotify(PlayerNotify notify);
    }
}
