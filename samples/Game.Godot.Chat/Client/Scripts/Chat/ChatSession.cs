using Godot;
using Shared.Contracts.Chat;
using Client.Login;

namespace Client.Chat
{
    public partial class ChatSession : Node
    {
        public LoginClient? LoginClient { get; set; }
        public LoginReply? LoginReply { get; set; }
    }
}
