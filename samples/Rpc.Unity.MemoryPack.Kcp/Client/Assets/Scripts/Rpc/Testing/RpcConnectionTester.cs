#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using Game.Rpc.Contracts;
using Client.Generated;
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

    public class RpcConnectionTester : MonoBehaviour
    {
        [SerializeField] private RpcEndpointSettings _endpoint = RpcEndpointSettings.CreateKcp("127.0.0.1", 20000);

        [Header("Login")] public string Account = "a";
        public string Password = "b";

        public float RequestIntervalSeconds = 1f;
        public bool AutoConnect = true;

        private CancellationTokenSource? _lifetime;
        private Task _cleanupTask = Task.CompletedTask;
        private bool _shuttingDown;

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
        }

        protected virtual RpcClientOptions CreateClientOptions() =>
            new RpcClientOptions(new KcpTransport(_endpoint.Host, _endpoint.Port), new MemoryPackRpcSerializer());

        [ContextMenu("Connect And Test")]
        public Task ConnectAndTestAsync()
        {
            if (_shuttingDown)
                return _cleanupTask;
            if (_lifetime is not null)
                return Task.CompletedTask;

            var lifetime = new CancellationTokenSource();
            _lifetime = lifetime;
            var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Debug.Log($"[KCP] Connecting to {_endpoint.Host}:{_endpoint.Port}");
            _cleanupTask = RunSessionAsync(lifetime, started);
            return started.Task;
        }

        private async Task RunSessionAsync(CancellationTokenSource lifetime, TaskCompletionSource<bool> started)
        {
            var token = lifetime.Token;
            try
            {
                var context = SynchronizationContext.Current
                    ?? throw new InvalidOperationException("Start the RPC tester on the Unity main thread.");
                var callbacks = new RpcClient.RpcNotificationBindings();
                callbacks.Add(new PlayerCallbacks(context, token));
                var connection = new RpcClient(CreateClientOptions(), callbacks);
                void OnDisconnected(Exception? error) => context.Post(_ =>
                {
                    if (token.IsCancellationRequested)
                        return;
                    if (error is null)
                        Debug.Log("[KCP] Disconnected.");
                    else
                        Debug.LogWarning($"[KCP] Disconnected: {error.Message}");
                    lifetime.Cancel();
                }, null);

                connection.Disconnected += OnDisconnected;
                var requests = Task.CompletedTask;
                try
                {
                    // Connect owns initialization until it returns. Shutdown cancels it before disposing the client.
                    await connection.ConnectAsync(token);
                    token.ThrowIfCancellationRequested();
                    requests = RunRequestsAsync(connection, started, token);
                    await Task.WhenAny(requests, Task.Delay(Timeout.Infinite, token));
                }
                finally
                {
                    connection.Disconnected -= OnDisconnected;
                    lifetime.Cancel();
                    try
                    {
                        await connection.DisposeAsync();
                    }
                    finally
                    {
                        await requests;
                    }
                }
            }
            catch (Exception ex)
            {
                if (!_shuttingDown)
                    Debug.LogError($"[KCP] Session failed: {ex}");
            }
            finally
            {
                _lifetime = null;
                lifetime.Dispose();
                started.TrySetResult(false);
            }
        }

        private async Task RunRequestsAsync(RpcClient connection, TaskCompletionSource<bool> started, CancellationToken token)
        {
            try
            {
                var player = new PlayerServiceClient(connection.Runtime);
                var reply = await player.LoginAsync(new LoginRequest { Account = Account, Password = Password }, token);
                token.ThrowIfCancellationRequested();
                Debug.Log($"[KCP] Login ok: account={Account}, code={reply.Code}, token={reply.Token}");
                started.TrySetResult(true);

                var interval = TimeSpan.FromSeconds(Mathf.Max(0.1f, RequestIntervalSeconds));
                while (!token.IsCancellationRequested)
                {
                    var step = await player.IncrStep(new StepRequest(), token);
                    token.ThrowIfCancellationRequested();
                    Debug.Log($"[KCP] {Account} step={step.Step}");
                    await Task.Delay(interval, token);
                }
            }
            catch (Exception) when (token.IsCancellationRequested)
            {
                // Both canceled calls and calls interrupted by connection disposal finish this session.
            }
            catch (Exception ex) when (started.Task.IsCompleted)
            {
                Debug.LogWarning($"[KCP] Polling failed: {ex.Message}");
            }
        }

        private void BeginShutdown()
        {
            _shuttingDown = true;
            _lifetime?.Cancel();
        }

        private sealed class PlayerCallbacks : RpcClient.PlayerNotificationsBase
        {
            private readonly SynchronizationContext _context;
            private readonly CancellationToken _token;

            public PlayerCallbacks(SynchronizationContext context, CancellationToken token)
            {
                _context = context;
                _token = token;
            }

            public override void OnNotify(PlayerNotify notify) => _context.Post(_ =>
            {
                if (!_token.IsCancellationRequested)
                    Debug.Log($"[KCP] Push: {notify.Message}");
            }, null);
        }
    }
}
