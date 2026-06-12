using System.Threading.Tasks;
using Shared.Contracts;
using Lakona.Rpc.Core;

namespace Shared.Contracts.Chat
{
    [RpcService(RpcContractIds.Services.Login, NotificationContract = typeof(ILoginCallback))]
    public interface ILoginService
    {
        [RpcMethod(RpcContractIds.LoginServiceMethods.LoginAsync)]
        ValueTask<LoginReply> LoginAsync(LoginRequest req);
    }

    [RpcNotificationContract(typeof(ILoginService))]
    public interface ILoginCallback
    {
        [RpcNotification(RpcContractIds.LoginNotifications.UserJoined)]
        void OnUserJoined(ChatMember member);

        [RpcNotification(RpcContractIds.LoginNotifications.UserLeft)]
        void OnUserLeft(ChatUserLeft evt);
    }
}
