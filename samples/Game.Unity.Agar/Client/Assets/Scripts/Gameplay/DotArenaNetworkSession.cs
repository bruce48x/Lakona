#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using Rpc;
using Rpc.Generated;
using Shared.Interfaces;
using Lakona.Game.Abstractions.Sessions;
using Lakona.Game.Client;
using Lakona.Rpc.Client;

namespace SampleClient.Gameplay
{
    internal sealed class DotArenaNetworkSession
    {
        private readonly Action<Exception?> _onDisconnected;
        private readonly LakonaGameClient _gameClient = new LakonaGameClient();
        private RpcClient? _controlConnection;
        private ILoginService? _loginService;
        private IPlayerService? _controlPlayerService;
        private RpcClient? _realtimeConnection;
        private IBattleService? _battleService;
        private string _playerId = string.Empty;
        private string _token = string.Empty;
        private string _sessionId = string.Empty;
        private long _sessionGeneration;
        private string _realtimeRoomId = string.Empty;
        private string _realtimeMatchId = string.Empty;
        private bool _ignoreControlDisconnect;
        private bool _ignoreRealtimeDisconnect;

        public DotArenaNetworkSession(Action<Exception?> onDisconnected)
        {
            _onDisconnected = onDisconnected;
        }

        public bool IsConnected { get; private set; }

        public bool IsConnecting { get; private set; }

        public bool IsRealtimeConnected { get; private set; }

        public bool IsRealtimeConnecting { get; private set; }

        public bool CanSubmitGameplayInput => IsRealtimeConnected;

        public async Task<LoginReply> ConnectAndLoginAsync(
            string host,
            int port,
            string path,
            string account,
            string password,
            bool guestLogin,
            bool reconnect,
            IControlCallback callback,
            CancellationToken cancellationToken)
        {
            if (IsConnecting)
            {
                throw new InvalidOperationException("Connection attempt is already in progress.");
            }

            IsConnecting = true;
            try
            {
                var callbacks = new RpcClient.RpcNotificationBindings();
                callbacks.Add((IControlCallback)callback);

                _controlConnection = Rpc.WebSocketRpcClientFactory.Create(host, port, path, callbacks);
                _controlConnection.Disconnected += HandleControlDisconnected;

                await _controlConnection.ConnectAsync(cancellationToken);
                await _gameClient.HandshakeAsync(_controlConnection.Runtime, new GameClientHello
                {
                    ClientRuntime = "unity",
                    Platform = "unity",
                    GameVersion = "agar"
                }, cancellationToken).ConfigureAwait(false);

                _loginService = _controlConnection.Api.Shared.Login;
                _controlPlayerService = _controlConnection.Api.Shared.Player;
                var reply = await _loginService.LoginAsync(new LoginRequest
                {
                    Account = account,
                    Password = password,
                    GuestLogin = guestLogin,
                    Reconnect = reconnect
                });

                if (reply.Code != 0)
                {
                    await DisposeControlAsync(logout: false).ConfigureAwait(false);

                    return reply;
                }

                _playerId = reply.PlayerId;
                _token = reply.Token;
                _sessionId = reply.SessionId;
                _sessionGeneration = reply.SessionGeneration;
                IsConnected = true;
                return reply;
            }
            catch
            {
                await DisposeControlAsync(logout: false).ConfigureAwait(false);
                throw;
            }
            finally
            {
                IsConnecting = false;
            }
        }

        public async Task SubmitInputAsync(InputMessage input)
        {
            if (_battleService == null)
            {
                return;
            }

            await _battleService.SubmitInputAsync(input).ConfigureAwait(false);
        }

        public async Task StartMatchmakingAsync(CancellationToken cancellationToken = default)
        {
            if (_controlPlayerService == null || string.IsNullOrWhiteSpace(_playerId))
            {
                return;
            }

            await _controlPlayerService.StartMatchmakingAsync(new MatchmakingRequest
            {
                PlayerId = _playerId,
                Token = _token
            }).ConfigureAwait(false);
        }

        public async Task CancelMatchmakingAsync(CancellationToken cancellationToken = default)
        {
            if (_controlPlayerService == null || string.IsNullOrWhiteSpace(_playerId))
            {
                return;
            }

            await _controlPlayerService.CancelMatchmakingAsync(new CancelMatchmakingRequest
            {
                PlayerId = _playerId,
                Token = _token
            }).ConfigureAwait(false);
        }

        public async Task<LeaderboardReply> GetLeaderboardAsync(int topN, CancellationToken cancellationToken = default)
        {
            if (_controlPlayerService == null)
            {
                return new LeaderboardReply
                {
                    Code = 1,
                    Message = "Not connected."
                };
            }

            return await _controlPlayerService.GetLeaderboardAsync(new LeaderboardRequest
            {
                TopN = topN
            }).ConfigureAwait(false);
        }

        public async Task<bool> EnsureRealtimeConnectedAsync(
            RealtimeConnectionInfo realtimeConnection,
            IBattleCallback callback,
            CancellationToken cancellationToken)
        {
            if (realtimeConnection == null)
            {
                return false;
            }

            if (realtimeConnection.Transport != RealtimeTransportKind.Kcp)
            {
                return false;
            }

            if (IsRealtimeConnected &&
                string.Equals(_realtimeRoomId, realtimeConnection.RoomId, StringComparison.Ordinal) &&
                string.Equals(_realtimeMatchId, realtimeConnection.MatchId, StringComparison.Ordinal))
            {
                return true;
            }

            if (IsRealtimeConnecting &&
                string.Equals(_realtimeRoomId, realtimeConnection.RoomId, StringComparison.Ordinal) &&
                string.Equals(_realtimeMatchId, realtimeConnection.MatchId, StringComparison.Ordinal))
            {
                return false;
            }

            await DisposeRealtimeAsync().ConfigureAwait(false);

            IsRealtimeConnecting = true;
            _realtimeRoomId = realtimeConnection.RoomId ?? string.Empty;
            _realtimeMatchId = realtimeConnection.MatchId ?? string.Empty;

            try
            {
                var callbacks = new RpcClient.RpcNotificationBindings();
                callbacks.Add((IBattleCallback)callback);

                _realtimeConnection = Rpc.KcpRpcClientFactory.Create(realtimeConnection.Host, realtimeConnection.Port, callbacks);
                _realtimeConnection.Disconnected += HandleRealtimeDisconnected;

                await _realtimeConnection.ConnectAsync(cancellationToken).ConfigureAwait(false);
                await _gameClient.HandshakeAsync(_realtimeConnection.Runtime, new GameClientHello
                {
                    ClientRuntime = "unity-realtime",
                    Platform = "unity",
                    GameVersion = "agar"
                }, cancellationToken).ConfigureAwait(false);

                _battleService = _realtimeConnection.Api.Shared.Battle;
                var reply = await _battleService.AttachRealtimeAsync(new RealtimeAttachRequest
                {
                    PlayerId = _playerId,
                    Token = string.IsNullOrWhiteSpace(realtimeConnection.SessionToken) ? _token : realtimeConnection.SessionToken,
                    RoomId = realtimeConnection.RoomId ?? string.Empty,
                    MatchId = realtimeConnection.MatchId ?? string.Empty
                }).ConfigureAwait(false);

                if (reply.Code != 0)
                {
                    await DisposeRealtimeAsync().ConfigureAwait(false);
                    return false;
                }

                IsRealtimeConnected = true;
                return true;
            }
            finally
            {
                IsRealtimeConnecting = false;
            }
        }

        public async Task DisposeAsync(bool logout = true)
        {
            await DisposeRealtimeAsync().ConfigureAwait(false);
            await DisposeControlAsync(logout).ConfigureAwait(false);
        }

        private async Task DisposeControlAsync(bool logout)
        {
            if (_controlConnection == null)
            {
                _loginService = null;
                _controlPlayerService = null;
                IsConnected = false;
                IsConnecting = false;
                return;
            }

            var connection = _controlConnection;
            var playerService = _controlPlayerService;
            var shouldLogout = logout && IsConnected && playerService != null;

            _controlConnection = null;
            _ignoreControlDisconnect = true;
            connection.Disconnected -= HandleControlDisconnected;

            try
            {
                if (shouldLogout)
                {
                    await playerService!.LogoutAsync(new LogoutRequest()).ConfigureAwait(false);
                }
            }
            catch
            {
            }

            try
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
            }
            finally
            {
                _ignoreControlDisconnect = false;
                _loginService = null;
                _controlPlayerService = null;
                if (logout)
                {
                    _playerId = string.Empty;
                    _token = string.Empty;
                    _sessionId = string.Empty;
                    _sessionGeneration = 0;
                }

                IsConnected = false;
                IsConnecting = false;
            }
        }

        public async Task DisposeRealtimeAsync()
        {
            if (_realtimeConnection == null)
            {
                _battleService = null;
                IsRealtimeConnected = false;
                IsRealtimeConnecting = false;
                _realtimeRoomId = string.Empty;
                _realtimeMatchId = string.Empty;
                return;
            }

            var connection = _realtimeConnection;
            _realtimeConnection = null;
            _ignoreRealtimeDisconnect = true;
            connection.Disconnected -= HandleRealtimeDisconnected;

            try
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
            }
            finally
            {
                _ignoreRealtimeDisconnect = false;
                _battleService = null;
                IsRealtimeConnected = false;
                IsRealtimeConnecting = false;
                _realtimeRoomId = string.Empty;
                _realtimeMatchId = string.Empty;
            }
        }

        private void HandleControlDisconnected(Exception? ex)
        {
            if (_ignoreControlDisconnect)
            {
                return;
            }

            IsConnected = false;
            _controlConnection = null;
            _loginService = null;
            _controlPlayerService = null;
            _onDisconnected(ex);
        }

        private void HandleRealtimeDisconnected(Exception? ex)
        {
            if (_ignoreRealtimeDisconnect)
            {
                return;
            }

            IsRealtimeConnected = false;
            _battleService = null;
            _realtimeRoomId = string.Empty;
            _realtimeMatchId = string.Empty;

            if (!IsConnected)
            {
                _onDisconnected(ex);
            }
        }
    }
}
