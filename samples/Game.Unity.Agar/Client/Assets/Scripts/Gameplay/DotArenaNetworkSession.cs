#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using Rpc;
using Client.Generated;
using Shared.Interfaces;
using Lakona.Game.Client.ReliablePush;
using Lakona.Game.Client.Sessions;
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
        private string _realtimeRoomId = string.Empty;
        private string _realtimeMatchId = string.Empty;
        private string _realtimeSessionToken = string.Empty;
        private string _realtimeSessionId = string.Empty;
        private long _controlRpcSerial;
        private long _realtimeRpcSerial;
#if UNITY_INCLUDE_TESTS
        private bool _networkGateOpen = true;
        private readonly TestTransportGate _testTransportGate = new TestTransportGate();
#endif
        private bool _ignoreControlDisconnect;
        private bool _ignoreRealtimeDisconnect;
        private bool _realtimeRecoveryObserved;
        private bool _realtimeReplayRefreshInProgress;
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

        public string ControlSessionId => _sessionId;
        public string RealtimeSessionId => _realtimeSessionId;
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
                    GuestLogin = guestLogin
                });

                if (reply.Code != 0)
                {
                    await DisposeControlAsync(logout: false).ConfigureAwait(false);

                    return reply;
                }

                _playerId = reply.PlayerId;
                _token = reply.Token;
                _sessionId = _controlConnection.Snapshot.SessionId ?? string.Empty;
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

        public async Task SubmitMatchResultAsync(FrameSyncMatchResult result)
        {
            if (_battleService == null)
            {
                return;
            }

            await _battleService.SubmitMatchResultAsync(result).ConfigureAwait(false);
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

        public async Task<RealtimeAttachReply?> EnsureRealtimeConnectedAsync(
            RealtimeConnectionInfo realtimeConnection,
            IBattleCallback callback,
            CancellationToken cancellationToken)
        {
#if UNITY_INCLUDE_TESTS
            if (!_networkGateOpen)
            {
                return null;
            }
#endif
            if (realtimeConnection == null)
            {
                return null;
            }

            if (realtimeConnection.Transport != RealtimeTransportKind.Kcp)
            {
                return null;
            }

            if (IsRealtimeConnected &&
                string.Equals(_realtimeRoomId, realtimeConnection.RoomId, StringComparison.Ordinal) &&
                string.Equals(_realtimeMatchId, realtimeConnection.MatchId, StringComparison.Ordinal))
            {
                return new RealtimeAttachReply
                {
                    Code = 0,
                    PlayerId = _playerId,
                    RoomId = _realtimeRoomId,
                    MatchId = _realtimeMatchId
                };
            }

            if (IsRealtimeConnecting &&
                string.Equals(_realtimeRoomId, realtimeConnection.RoomId, StringComparison.Ordinal) &&
                string.Equals(_realtimeMatchId, realtimeConnection.MatchId, StringComparison.Ordinal))
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(_realtimeMatchId) &&
                !string.Equals(_realtimeMatchId, realtimeConnection.MatchId, StringComparison.Ordinal))
            {
                _realtimeSessionId = string.Empty;
            }

            await DisposeRealtimeAsync().ConfigureAwait(false);

            IsRealtimeConnecting = true;
            _realtimeRoomId = realtimeConnection.RoomId ?? string.Empty;
            _realtimeMatchId = realtimeConnection.MatchId ?? string.Empty;
            _realtimeSessionToken = string.IsNullOrWhiteSpace(realtimeConnection.SessionToken)
                ? _token
                : realtimeConnection.SessionToken;

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
                    Token = _realtimeSessionToken,
                    RoomId = realtimeConnection.RoomId ?? string.Empty,
                    MatchId = realtimeConnection.MatchId ?? string.Empty
                }).ConfigureAwait(false);

                if (reply.Code != 0)
                {
                    await DisposeRealtimeAsync().ConfigureAwait(false);
                    return null;
                }

                _realtimeSessionId = _realtimeConnection.Snapshot.SessionId ?? string.Empty;
                IsRealtimeConnected = true;
                return reply;
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

        public bool ShouldRefreshRealtimeReplayAfterRecovery()
        {
            if (_realtimeConnection == null || _battleService == null || !IsRealtimeConnected)
            {
                return false;
            }

            var phase = _realtimeConnection.Snapshot.Phase;
            if (phase == ClientSessionPhase.Reconnecting)
            {
                _realtimeRecoveryObserved = true;
                return false;
            }

            return _realtimeRecoveryObserved &&
                   phase == ClientSessionPhase.Active &&
                   !_realtimeReplayRefreshInProgress;
        }

        public async Task<RealtimeAttachReply?> RefreshRealtimeReplayAfterRecoveryAsync()
        {
            if (!ShouldRefreshRealtimeReplayAfterRecovery())
            {
                return null;
            }

            var battleService = _battleService;
            if (battleService == null)
            {
                return null;
            }

            _realtimeRecoveryObserved = false;
            _realtimeReplayRefreshInProgress = true;
            try
            {
                var reply = await battleService.AttachRealtimeAsync(new RealtimeAttachRequest
                {
                    PlayerId = _playerId,
                    Token = _realtimeSessionToken,
                    RoomId = _realtimeRoomId,
                    MatchId = _realtimeMatchId
                }).ConfigureAwait(false);

                return reply.Code == 0 ? reply : null;
            }
            finally
            {
                _realtimeReplayRefreshInProgress = false;
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
                _realtimeSessionToken = string.Empty;
                _realtimeRecoveryObserved = false;
                _realtimeReplayRefreshInProgress = false;
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
                _realtimeSessionToken = string.Empty;
                _realtimeRecoveryObserved = false;
                _realtimeReplayRefreshInProgress = false;
            }
        }

        private void HandleControlDisconnected(Exception? ex)
        {
            if (_ignoreControlDisconnect)
            {
                return;
            }

            IsConnected = false;
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
