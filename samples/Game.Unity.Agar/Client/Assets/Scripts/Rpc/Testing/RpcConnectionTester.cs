#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using Client.Generated;
using Shared.Interfaces;
using UnityEngine;

namespace Rpc.Testing
{
    [Serializable]
    public sealed class RpcEndpointSettings
    {
        public string Host = "127.0.0.1";
        public int Port = 20000;
        public string Path = string.Empty;

        public static RpcEndpointSettings CreateDefault() => new RpcEndpointSettings { Host = "127.0.0.1", Port = 20000, Path = "/ws" };
        
        public static RpcEndpointSettings CreateWebSocket(string host, int port, string path = "/ws")
        {
            return new RpcEndpointSettings
            {
                Host = host,
                Port = port,
                Path = path
            };
        }
        
        public string GetWebSocketUrl()
        {
            var normalizedPath = string.IsNullOrWhiteSpace(Path)
                ? "/ws"
                : Path.StartsWith("/", StringComparison.Ordinal) ? Path : "/" + Path;
            return $"ws://{Host}:{Port}{normalizedPath}";
        }
    }

    public sealed class RpcConnectionTester : MonoBehaviour
    {
        [SerializeField]
        private RpcEndpointSettings _endpoint = RpcEndpointSettings.CreateWebSocket("127.0.0.1", 20000);

        [Header("Login")] public string Account = "";
        public string Password = "";

        public float RequestIntervalSeconds = 1f;
        public bool AutoConnect = true;

        private readonly CancellationTokenSource _cts = new();
        private bool _cleanupStarted;
        private LakonaGameClient? _connection;
        private ILoginService? _login;
        private IPlayerService? _player;
        private string _playerId = string.Empty;
        private Task? _pollingTask;
        private bool _stopped;

        private async void Start()
        {
            ApplyLaunchOverrides();

            if (!Application.isEditor || !AutoConnect)
                return;

            await ConnectAndTestAsync();
        }

        private void OnDisable()
        {
            BeginShutdown();
        }

        private void OnDestroy()
        {
            BeginShutdown();
            _cts.Dispose();
        }

        [ContextMenu("Connect And Test")]
        public async Task ConnectAndTestAsync()
        {
            if (_cleanupStarted || _connection is not null)
                return;

            Debug.Log($"[WS] Connecting to {_endpoint.GetWebSocketUrl()}");

            try
            {
                _connection = new LakonaGameClient(
                    WebSocketRpcClientFactory.CreateOptions(_endpoint.Host, _endpoint.Port, _endpoint.Path),
                    new PlayerCallbacks(this));
                _connection.Disconnected += OnDisconnected;
                await _connection.ConnectAsync(_cts.Token);
                _login = _connection.Api.Shared.Login;
                _player = _connection.Api.Shared.Player;

                var reply = await _login.LoginAsync(new LoginRequest
                {
                    Account = Account,
                    Password = Password,
                    GuestLogin = string.IsNullOrWhiteSpace(Account) || string.IsNullOrWhiteSpace(Password)
                });

                if (!string.IsNullOrWhiteSpace(reply.Account))
                {
                    Account = reply.Account;
                }

                if (!string.IsNullOrWhiteSpace(reply.Password))
                {
                    Password = reply.Password;
                }

                _playerId = reply.PlayerId;
                Debug.Log($"[WS] Login ok: account={Account}, playerId={reply.PlayerId}, code={reply.Code}, token={reply.Token}");
                await _player.StartMatchmakingAsync(new MatchmakingRequest
                {
                    PlayerId = reply.PlayerId,
                    Token = reply.Token
                });
                _pollingTask = RunPollingAsync();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[WS] Connect failed: {ex}");
                await CleanupAsync();
            }
        }

        private async Task RunPollingAsync()
        {
            var interval = Mathf.Max(0.1f, RequestIntervalSeconds);

            while (!_cts.IsCancellationRequested && !_stopped)
                try
                {
                    var leaderboard = await _player!.GetLeaderboardAsync(new LeaderboardRequest
                    {
                        TopN = 5
                    });
                    if (_cts.IsCancellationRequested || _stopped)
                        return;

                    Debug.Log($"{Account} Leaderboard entries={leaderboard.Entries.Count}");
                    await Task.Delay(TimeSpan.FromSeconds(interval), _cts.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[WS] Polling failed: {ex.Message}");
                    return;
                }
        }

        private void BeginShutdown()
        {
            if (_cleanupStarted)
                return;

            _cleanupStarted = true;
            _stopped = true;
            _cts.Cancel();

            if (_connection is not null)
                _connection.Disconnected -= OnDisconnected;

            _ = CleanupAsync();
        }

        private async Task CleanupAsync()
        {
            if (_pollingTask is not null)
                try
                {
                    await _pollingTask;
                }
                catch (OperationCanceledException)
                {
                }

            if (_connection is not null)
            {
                await _connection.DisposeAsync();
                _connection = null;
            }
        }

        private void OnDisconnected(Exception? ex)
        {
            if (_stopped)
                return;

            _stopped = true;
            if (_connection is not null)
                _connection.Disconnected -= OnDisconnected;

            _ = CleanupAsync();

            if (ex is null)
                Debug.Log("[WS] Disconnected.");
            else
                Debug.LogWarning($"[WS] Disconnected: {ex.Message}");
        }

        private void ApplyLaunchOverrides()
        {
            var launchArguments = Rpc.RpcLaunchArguments.ReadCurrentProcess();
            launchArguments.ApplyTo(ref _endpoint.Host, ref _endpoint.Port, ref _endpoint.Path);
            launchArguments.ApplyCredentials(ref Account, ref Password);

            if (launchArguments.HasOverrides)
            {
                Debug.Log($"[LaunchArgs] RpcConnectionTester host={_endpoint.Host}, port={_endpoint.Port}, path={_endpoint.Path}, account={Account}");
            }
        }

        private sealed class PlayerCallbacks : IPlayerCallback
        {
            public PlayerCallbacks(RpcConnectionTester owner)
            {
            }

            public void OnMatchmakingStatus(MatchmakingStatusUpdate matchmakingStatus)
            {
                Debug.Log($"[WS] Matchmaking state={matchmakingStatus.State}, room={matchmakingStatus.RoomId}, queue={matchmakingStatus.QueuePosition}/{matchmakingStatus.QueueSize}, matched={matchmakingStatus.MatchedPlayerCount}/{matchmakingStatus.RoomCapacity}, message={matchmakingStatus.Message}");
            }

            public void OnMatchProgress(MatchProgressUpdate update)
            {
            }
        }
    }
}
