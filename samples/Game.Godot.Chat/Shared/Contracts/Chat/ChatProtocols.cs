using System.Threading.Tasks;
using Shared.Contracts;
using Lakona.Rpc.Core;

namespace Shared.Contracts.Chat
{
    [RpcService(RpcContractIds.Services.Chat, NotificationContract = typeof(IChatCallback))]
    public interface IChatService
    {
        [RpcMethod(RpcContractIds.ChatServiceMethods.BindAsync)]
        ValueTask BindAsync(ChatBindRequest req);

        [RpcMethod(RpcContractIds.ChatServiceMethods.SendAsync)]
        ValueTask SendAsync(ChatSendRequest req);
    }

    [RpcNotificationContract(typeof(IChatService))]
    public interface IChatCallback
    {
        [RpcNotification(RpcContractIds.ChatNotifications.MessageReceived)]
        void OnMessageReceived(ChatMessage msg);
    }
}
