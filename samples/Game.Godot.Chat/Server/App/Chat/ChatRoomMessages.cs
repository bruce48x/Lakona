using Shared.Contracts.Chat;

namespace Server.App.Chat
{
    public static class ChatRoomIds
    {
        public const string Global = "chat-room/global";
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
