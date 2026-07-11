#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using Rpc;
using Client.Generated;
using Shared.Interfaces;
using Lakona.Game.Client.ReliablePush;
#if UNITY_INCLUDE_TESTS
using Rpc.Testing;
#endif

namespace SampleClient.Gameplay
{
    internal sealed class DotArenaNetworkSession
    {
        private readonly Action<Exception?> _onDisconnected;
        private LakonaGameClient? _controlConnection;
        private ILoginService? _loginService;
        private IPlayerService? _controlPlayerService;
        private LakonaGameClient? _realtimeConnection;
        private IBattleService? _battleService;
        private string _playerId = string.Empty;
        private string _token = string.Empty;
        private string _sessionId = string.Empty;
        private long _sessionGeneration;
        private string _realtimeRoomId = string.Empty;
        private string _realtimeMatchId = string.Empty;
        private string _realtimeSessionId = string.Empty;
        private long _realtimeSessionGeneration;
        private long _controlRpcSerial;
        private long _realtimeRpcSerial;
        private DateTime _controlReconnectDeadlineUtc;
        private TimeSpan _controlResumeWindow = TimeSpan.FromSeconds(60);
#if UNITY_INCLUDE_TESTS
        private bool _networkGateOpen = true;
        private readonly TestTransportGate _testTransportGate = new TestTransportGate();
#endif
        private bool _ignoreControlDisconnect;
        private bool _ignoreRealtimeDisconnect;
        private readonly InMemoryReliablePushCursorStore _controlReliablePushCursors = new InMemoryReliablePushCursorStore();

        public DotArenaNetworkSession(Action<Exception?> onDisconnected)
        {
            _onDisconnected = onDisconnected;
        }

        public bool IsConnected { get; private set; }

        public bool IsConnecting { get; private set; }

        public bool IsRealtimeConnected { get; private set; }

        public bool IsRealtimeConnecting { get; private set; }

        public bool CanSubmitGameplayInput => IsRealtimeConnected;

        public DateTime ControlReconnectDeadlineUtc => _controlReconnectDeadlineUtc;
        public string ControlSessionId => _sessionId;
        public long ControlSessionGeneration => _sessionGeneration;
        public string RealtimeSessionId => _realtimeSessionId;
        public long RealtimeSessionGeneration => _realtimeSessionGeneration;
        public long ControlRpcSerial => _controlRpcSerial;
        public long RealtimeRpcSerial => _realtimeRpcSerial;
        public bool ControlReliablePushEnabled => _controlConnection?.ReliablePushEnabled ?? false;
        public bool RealtimeReliablePushEnabled => _realtimeConnection?.ReliablePushEnabled ?? false;
        public long ControlLastReliableSequence => _controlConnection?.Snapshot.LastReliableSequence ?? 0;

        public async Task<LoginReply> ConnectAndLoginAsync(
            string host,
            int port,
            string path,
            string account,
            string password,
            bool guestLogin,
            bool reconnect,
            IPlayerCallback callback,
            CancellationToken cancellationToken)
        {
#if UNITY_INCLUDE_TESTS
            if (!_networkGateOpen)
            {
                throw new InvalidOperationException("The test network gate is closed.");
            }
#endif
            if (IsConnecting)
            {
                throw new InvalidOperationException("Connection attempt is already in progress.");
            }

            IsConnecting = true;
            try
            {
                var controlOptions = WebSocketRpcClientFactory.CreateOptions(host, port, path
#if UNITY_INCLUDE_TESTS
                    , _testTransportGate
#endif
                );
                controlOptions.ReliablePushCursorStore = _controlReliablePushCursors;
                _controlConnection = new LakonaGameClient(
                    controlOptions,
                    callback);
                _controlRpcSerial += 1;
                _controlConnection.Disconnected += HandleControlDisconnected;

                await _controlConnection.ConnectAsync(cancellationToken).ConfigureAwait(false);
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
                _sessionId = _controlConnection.Snapshot.SessionId ?? string.Empty;
                _sessionGeneration = _controlConnection.Snapshot.SessionGeneration;
                IsConnected = true;
                _controlResumeWindow = _controlConnection.SessionResumeWindow;
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
#if UNITY_INCLUDE_TESTS
            if (!_networkGateOpen)
            {
                return false;
            }
#endif
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

            if (!string.IsNullOrWhiteSpace(_realtimeMatchId) &&
                !string.Equals(_realtimeMatchId, realtimeConnection.MatchId, StringComparison.Ordinal))
            {
                _realtimeSessionId = string.Empty;
                _realtimeSessionGeneration = 0;
            }

            await DisposeRealtimeAsync().ConfigureAwait(false);

            IsRealtimeConnecting = true;
            _realtimeRoomId = realtimeConnection.RoomId ?? string.Empty;
            _realtimeMatchId = realtimeConnection.MatchId ?? string.Empty;

            try
            {
                _realtimeConnection = new LakonaGameClient(
                    KcpRpcClientFactory.CreateOptions(realtimeConnection.Host, realtimeConnection.Port
#if UNITY_INCLUDE_TESTS
                        , _testTransportGate
#endif
                    ),
                    callback);
                _realtimeRpcSerial += 1;
                _realtimeConnection.Disconnected += HandleRealtimeDisconnected;

                await _realtimeConnection.ConnectAsync(cancellationToken).ConfigureAwait(false);
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

                _realtimeSessionId = _realtimeConnection.Snapshot.SessionId ?? string.Empty;
                _realtimeSessionGeneration = _realtimeConnection.Snapshot.SessionGeneration;
                IsRealtimeConnected = true;
                return true;
            }
            catch
            {
                await DisposeRealtimeAsync().ConfigureAwait(false);
                throw;
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
            _controlReconnectDeadlineUtc = DateTime.UtcNow.Add(
                _controlResumeWindow);
            _loginService = null;
            _controlPlayerService = null;
            _ = DisposeControlAfterDisconnectAsync();
            _onDisconnected(ex);
        }

#if UNITY_INCLUDE_TESTS
        public async Task SetNetworkGateForTestAsync(bool open)
        {
            if (_networkGateOpen == open)
            {
                return;
            }

            _networkGateOpen = open;
            await _testTransportGate.SetOpenAsync(open).ConfigureAwait(false);
        }
#endif

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
            _ = DisposeRealtimeAfterDisconnectAsync();

            if (!IsConnected)
            {
                _onDisconnected(ex);
            }
        }

        private async Task DisposeControlAfterDisconnectAsync()
        {
            try
            {
                await DisposeControlAsync(logout: false).ConfigureAwait(false);
            }
            catch
            {
            }
        }

        private async Task DisposeRealtimeAfterDisconnectAsync()
        {
            try
            {
                await DisposeRealtimeAsync().ConfigureAwait(false);
            }
            catch
            {
            }
        }
    }
}
