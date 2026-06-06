#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using Game.Rpc.Contracts;
using Rpc.Generated;
using Lakona.Rpc.Client;
using Lakona.Rpc.Transport.Kcp;
using Lakona.Rpc.Serializer.MemoryPack;
using UnityEngine;

namespace Rpc.Testing
{
    [Serializable]
    public sealed class RpcEndpointSettings
    {
        public string Host = "127.0.0.1";
        public int Port = 20000;

        public static RpcEndpointSettings CreateKcp(string host, int port)
        {
            return new RpcEndpointSettings
            {
                Host = host,
                Port = port
            };
        }
    }

    public sealed class RpcConnectionTester : MonoBehaviour
    {
        [SerializeField] private RpcEndpointSettings _endpoint = RpcEndpointSettings.CreateKcp("127.0.0.1", 20000);

        [Header("Login")] public string Account = "a";
        public string Password = "b";

        public float RequestIntervalSeconds = 1f;
        public bool AutoConnect = true;

        private readonly CancellationTokenSource _cts = new();
        private readonly RpcClient.RpcNotificationBindings _callbacks;
        private RpcClient? _connection;
        private IPlayerService? _player;
        private Task? _pollingTask;
        private bool _cleanupStarted;
        private bool _stopped;

        public RpcConnectionTester()
        {
            _callbacks = new RpcClient.RpcNotificationBindings();
            _callbacks.Add(new PlayerCallbacks(this));
        }

        private async void Start()
        {
            if (!AutoConnect)
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

            Debug.Log($"[KCP] Connecting to {_endpoint.Host}:{_endpoint.Port}");

            try
            {
                _connection = new RpcClient(
                    new RpcClientOptions(
                        new KcpTransport(_endpoint.Host, _endpoint.Port),
                        new MemoryPackRpcSerializer()),
                    _callbacks);
                await _connection.ConnectAsync(_cts.Token);
                _connection.Disconnected += OnDisconnected;
                _player = _connection.Api.Game.Player;

                var reply = await _player.LoginAsync(new LoginRequest
                {
                    Account = Account,
                    Password = Password
                });

                Debug.Log($"[KCP] Login ok: account={Account}, code={reply.Code}, token={reply.Token}");
                _pollingTask = RunPollingAsync();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[KCP] Connect failed: {ex}");
                await CleanupAsync();
            }
        }

        private async Task RunPollingAsync()
        {
            var interval = Mathf.Max(0.1f, RequestIntervalSeconds);

            while (!_cts.IsCancellationRequested && !_stopped)
            {
                try
                {
                    var step = await _player!.IncrStep(new StepRequest());
                    if (_cts.IsCancellationRequested || _stopped)
                        return;

                    Debug.Log($"[KCP] {Account} step={step.Step}");
                    await Task.Delay(TimeSpan.FromSeconds(interval), _cts.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[KCP] Polling failed: {ex.Message}");
                    return;
                }
            }
        }

            private void HandleNotify(string message)
        {
            if (_stopped)
                return;

            Debug.Log($"[KCP] Push: {message}");
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
            {
                try
                {
                    await _pollingTask;
                }
                catch (OperationCanceledException)
                {
                }
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
                Debug.Log("[KCP] Disconnected.");
            else
                Debug.LogWarning($"[KCP] Disconnected: {ex.Message}");
        }

        private sealed class PlayerCallbacks : RpcClient.PlayerNotificationsBase
        {
            private readonly RpcConnectionTester _owner;

            public PlayerCallbacks(RpcConnectionTester owner)
            {
                _owner = owner;
            }

            public override void OnNotify(PlayerNotify notify)
            {
                _owner.HandleNotify(notify.Message);
            }
        }
    }
}
