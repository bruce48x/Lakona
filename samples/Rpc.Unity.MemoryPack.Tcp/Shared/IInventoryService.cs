using System.Threading.Tasks;
using Lakona.Rpc.Core;

namespace Game.Rpc.Contracts
{
    [RpcService(RpcContractIds.Services.Inventory, NotificationContract = typeof(IInventoryNotifications))]
    public interface IInventoryService
    {
        [RpcMethod(RpcContractIds.InventoryServiceMethods.GetRevisionAsync)]
        ValueTask<RevisionReply> GetRevisionAsync(RevisionRequest req);

        [RpcMethod(RpcContractIds.InventoryServiceMethods.IncrRevision)]
        ValueTask<RevisionReply> IncrRevision(RevisionRequest req);
    }

    [RpcNotificationContract(typeof(IInventoryService))]
    public interface IInventoryNotifications
    {
        [RpcNotification(RpcContractIds.InventoryNotifications.OnInventoryNotify)]
        void OnInventoryNotify(InventoryNotify notify);
    }
}
