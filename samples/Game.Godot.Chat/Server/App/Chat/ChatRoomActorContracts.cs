using System.Threading;
using System.Threading.Tasks;
using Shared.Contracts.Chat;
using Lakona.Game.Server.Hotfix.Abstractions;

namespace Server.App.Chat
{
    public static class ChatRoomIds
    {
        public const string Global = "chat-room/global";
    }

    [HotfixActorContract(typeof(ChatRoomActor))]
    public interface IChatRoomActorContract
    {
        ValueTask<LoginReply> LoginAsync(ChatRoomLoginRequest request, CancellationToken cancellationToken = default);

        ValueTask BindChatAsync(ChatRoomBindRequest request, CancellationToken cancellationToken = default);

        ValueTask SendAsync(ChatRoomSendRequest request, CancellationToken cancellationToken = default);

        ValueTask LeaveAsync(ChatRoomLeaveRequest request, CancellationToken cancellationToken = default);
    }

    public sealed class ChatRoomLoginRequest
    {
        public string ConnectionId { get; set; } = "";

        public string PlayerName { get; set; } = "";

        public ILoginCallback LoginCallback { get; set; } = null!;
    }

    public sealed class ChatRoomBindRequest
    {
        public string ConnectionId { get; set; } = "";

        public IChatCallback ChatCallback { get; set; } = null!;
    }

    public sealed class ChatRoomSendRequest
    {
        public string ConnectionId { get; set; } = "";

        public string Text { get; set; } = "";
    }

    public sealed class ChatRoomLeaveRequest
    {
        public string ConnectionId { get; set; } = "";
    }
}
