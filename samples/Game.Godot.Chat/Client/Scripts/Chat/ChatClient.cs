using System;
using System.Threading.Tasks;
using Shared.Contracts.Chat;
using Client.Login;

namespace Client.Chat
{
    public sealed class ChatClient
    {
        private readonly LoginClient _loginClient;
        private readonly IChatService _chatService;

        public event Action<ChatMessage>? OnMessageReceived
        {
            add { _loginClient.OnMessageReceived += value; }
            remove { _loginClient.OnMessageReceived -= value; }
        }

        public ChatClient(LoginClient loginClient)
        {
            _loginClient = loginClient ?? throw new ArgumentNullException(nameof(loginClient));
            _chatService = loginClient.RpcClient.Api.Shared.Chat;
        }

        public async Task BindAsync()
        {
            await _chatService.BindAsync(new ChatBindRequest());
        }

        public async Task SendAsync(string text)
        {
            await _chatService.SendAsync(new ChatSendRequest { Text = text });
        }
    }
}
