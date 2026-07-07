using System;
using System.Threading;
using System.Threading.Tasks;
using Client.Generated;
using Shared.Contracts.Chat;
using Lakona.Game.Client;
using Lakona.Rpc.Client;

namespace Client.Login
{
    public sealed class LoginClient : ILoginCallback, IChatCallback, IAsyncDisposable
    {
        private readonly LakonaGameClient _gameClient;
        private ILoginService? _loginService;
        private bool _isConnected;

        public event Action<ChatMember>? OnUserJoined;
        public event Action<string>? OnUserLeft;
        public event Action? OnDisconnected;
        public event Action<ChatMessage>? OnMessageReceived;

        public bool IsConnected => _isConnected;
        public LakonaGameClient GameClient => _gameClient;

        public LoginClient(LakonaGameClientOptions options)
        {
            _gameClient = new LakonaGameClient(options, this);
            _gameClient.Disconnected += _ =>
            {
                _isConnected = false;
                OnDisconnected?.Invoke();
            };
        }

        public async Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            await _gameClient.ConnectAsync(cancellationToken);
            _loginService = _gameClient.Api.Shared.Login;
            _isConnected = true;
        }

        public async Task<LoginReply> LoginAsync(string playerName)
        {
            if (_loginService == null)
            {
                throw new InvalidOperationException("Not connected.");
            }

            return await _loginService.LoginAsync(new LoginRequest { PlayerName = playerName });
        }

        public async ValueTask DisposeAsync()
        {
            _isConnected = false;
            await _gameClient.DisposeAsync();
        }

        void ILoginCallback.OnUserJoined(ChatMember member)
        {
            OnUserJoined?.Invoke(member);
        }

        void ILoginCallback.OnUserLeft(ChatUserLeft evt)
        {
            OnUserLeft?.Invoke(evt.Name);
        }

        void IChatCallback.OnMessageReceived(ChatMessage msg)
        {
            OnMessageReceived?.Invoke(msg);
        }
    }
}
