using System;
using System.Threading;
using System.Threading.Tasks;
using Rpc.Generated;
using Shared.Contracts.Chat;
using Lakona.Rpc.Client;

namespace Client.Login
{
    public sealed class LoginClient : ILoginCallback, IChatCallback, IAsyncDisposable
    {
        private readonly RpcClient _rpcClient;
        private ILoginService? _loginService;
        private bool _isConnected;

        public event Action<ChatMember>? OnUserJoined;
        public event Action<string>? OnUserLeft;
        public event Action? OnDisconnected;
        public event Action<ChatMessage>? OnMessageReceived;

        public bool IsConnected => _isConnected;
        public RpcClient RpcClient => _rpcClient;

        public LoginClient(RpcClientOptions options)
        {
            var callbacks = new RpcClient.RpcNotificationBindings();
            callbacks.Add((ILoginCallback)this);
            callbacks.Add((IChatCallback)this);

            _rpcClient = new RpcClient(options, callbacks);
            _rpcClient.Disconnected += _ =>
            {
                _isConnected = false;
                OnDisconnected?.Invoke();
            };
        }

        public async Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            await _rpcClient.ConnectAsync(cancellationToken);
            _loginService = _rpcClient.Api.Shared.Login;
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
            await _rpcClient.DisposeAsync();
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
