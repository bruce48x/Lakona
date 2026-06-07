using System.Threading.Tasks;
using Lakona.Rpc.Core;

namespace Game.Rpc.Contracts
{
    [RpcService(RpcContractIds.Services.Quest, NotificationContract = typeof(IQuestNotifications))]
    public interface IQuestService
    {
        [RpcMethod(RpcContractIds.QuestServiceMethods.GetProgressAsync)]
        ValueTask<ProgressReply> GetProgressAsync(ProgressRequest req);

        [RpcMethod(RpcContractIds.QuestServiceMethods.IncrProgress)]
        ValueTask<ProgressReply> IncrProgress(ProgressRequest req);
    }

    [RpcNotificationContract(typeof(IQuestService))]
    public interface IQuestNotifications
    {
        [RpcNotification(RpcContractIds.QuestNotifications.OnQuestNotify)]
        void OnQuestNotify(QuestNotify notify);
    }
}
