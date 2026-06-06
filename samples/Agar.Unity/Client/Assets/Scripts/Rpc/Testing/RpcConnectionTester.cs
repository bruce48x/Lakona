#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using Rpc.Generated;
using Shared.Interfaces;
using Lakona.Rpc.Client;
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
        private readonly RpcClient.RpcNotificationBindings _callbacks;

        private readonly CancellationTokenSource _cts = new();
        private bool _cleanupStarted;
        private RpcClient? _connection;
        private IPlayerService? _player;
        private string _playerId = string.Empty;
        private Task? _pollingTask;
        private bool _stopped;
        private int _tick;

        public RpcConnectionTester()
        {
            _callbacks = new RpcClient.RpcNotificationBindings();
            _callbacks.Add(new PlayerCallbacks(this));
        }

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
                _connection = WebSocketRpcClientFactory.Create(_endpoint.Host, _endpoint.Port, _endpoint.Path, _callbacks);
                await _connection.ConnectAsync(_cts.Token);
                _connection.Disconnected += OnDisconnected;
                _player = _connection.Api.Shared.Player;

                var reply = await _player.LoginAsync(new LoginRequest
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
                    await _player!.SubmitInput(new InputMessage
                    {
                        PlayerId = _playerId,
                        MoveX = 1f,
                        MoveY = 0f,
                        Tick = ++_tick
                    });
                    if (_cts.IsCancellationRequested || _stopped)
                        return;

                    Debug.Log($"{Account} Input submitted");
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
            _connection = null;

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

        private sealed class PlayerCallbacks : RpcClient.PlayerCallbackBase
        {
            private readonly RpcConnectionTester _owner;

            public PlayerCallbacks(RpcConnectionTester owner)
            {
                _owner = owner;
            }

            public override void OnWorldState(WorldState worldState)
            {
                _owner.HandleWorldState(worldState);
            }

            public override void OnPlayerDead(PlayerDead deadEvent)
            {
                Debug.Log($"[WS] Player dead: {deadEvent.PlayerId} @ tick {deadEvent.Tick}");
            }

            public override void OnMatchEnd(MatchEnd matchEnd)
            {
                Debug.Log($"[WS] Match end: winner={matchEnd.WinnerPlayerId}, tick={matchEnd.Tick}");
            }

            public override void OnMatchmakingStatus(MatchmakingStatusUpdate matchmakingStatus)
            {
                Debug.Log($"[WS] Matchmaking state={matchmakingStatus.State}, room={matchmakingStatus.RoomId}, queue={matchmakingStatus.QueuePosition}/{matchmakingStatus.QueueSize}, matched={matchmakingStatus.MatchedPlayerCount}/{matchmakingStatus.RoomCapacity}, message={matchmakingStatus.Message}");
            }
        }

        private void HandleWorldState(WorldState worldState)
        {
            if (_stopped)
                return;

            Debug.Log($"[WS] WorldState tick={worldState.Tick}, players={worldState.Players.Count}");
        }
    }
}
