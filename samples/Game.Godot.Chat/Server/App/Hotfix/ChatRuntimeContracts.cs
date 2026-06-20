using Lakona.Rpc.Core;

namespace Server.App.Hotfix
{
    public interface IChatRuntimeService
    {
        [RpcMethod(ChatRuntimeMethodIds.SessionExpired)]
        ValueTask SessionExpiredAsync(ChatSessionExpiredRequest request);
    }

    public static class ChatRuntimeMethodIds
    {
        public const int SessionExpired = 1;
    }

    public sealed class ChatSessionExpiredRequest
    {
        public string ConnectionId { get; set; } = "";
    }
}
