using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Lakona.Game.Cluster;
using Lakona.Rpc.Client;
using Lakona.Rpc.Core;

namespace Lakona.Game.Cluster.Rpc
{
    public sealed class ClusterClientFactory : IClusterClientFactory, IAsyncDisposable
    {
        private readonly ConcurrentDictionary<ClientKey, Lazy<Task<RpcClientRuntime>>> _clients =
            new ConcurrentDictionary<ClientKey, Lazy<Task<RpcClientRuntime>>>();
        private readonly ClusterRpcChannel _channel;
        private readonly IRpcSerializer _serializer;
        private readonly ClusterClientFactoryOptions _options;
        private int _disposed;

        public ClusterClientFactory(
            ClusterRpcChannel channel,
            ClusterClientFactoryOptions? options = null)
        {
            _channel = channel ?? throw new ArgumentNullException(nameof(channel));
            _serializer = channel.Serializer;
            _options = options ?? new ClusterClientFactoryOptions();
        }

        public async ValueTask<IRpcClient> GetClientAsync(
            RouteLocation target,
            CancellationToken cancellationToken = default)
        {
            if (target is null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            var key = ClientKey.From(target);
            var candidate = new Lazy<Task<RpcClientRuntime>>(
                () => ConnectAsync(target),
                LazyThreadSafetyMode.ExecutionAndPublication);
            var selected = _clients.GetOrAdd(key, candidate);
            try
            {
                var runtime = await selected.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
                if (ReferenceEquals(candidate, selected))
                {
                    RemoveSuperseded(key);
                }

                return runtime;
            }
            catch
            {
                if (selected.Value.IsCompleted && !selected.Value.IsCompletedSuccessfully)
                {
                    ((ICollection<KeyValuePair<ClientKey, Lazy<Task<RpcClientRuntime>>>>)_clients)
                        .Remove(new KeyValuePair<ClientKey, Lazy<Task<RpcClientRuntime>>>(key, selected));
                }
                throw;
            }
        }

        private async Task<RpcClientRuntime> ConnectAsync(RouteLocation target)
        {
            using var timeout = CreateConnectTimeout(CancellationToken.None);
            var effectiveToken = timeout?.Token ?? CancellationToken.None;
            var transport = await _channel.ConnectAsync(target, effectiveToken).ConfigureAwait(false);
            var runtime = new RpcClientRuntime(
                transport,
                _serializer,
                _options.KeepAlive);
            var startTask = runtime.StartAsync(CancellationToken.None).AsTask();
            _ = startTask.ContinueWith(
                task => _ = task.Exception,
                TaskContinuationOptions.OnlyOnFaulted);

            if (Volatile.Read(ref _disposed) != 0)
            {
                await runtime.DisposeAsync().ConfigureAwait(false);
                throw new ObjectDisposedException(nameof(ClusterClientFactory));
            }

            return runtime;
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            var clients = _clients.ToArray();
            _clients.Clear();
            foreach (var client in clients)
            {
                if (!client.Value.IsValueCreated)
                {
                    continue;
                }

                try
                {
                    await (await client.Value.Value.ConfigureAwait(false))
                        .DisposeAsync()
                        .ConfigureAwait(false);
                }
                catch
                {
                }
            }
        }

        private void RemoveSuperseded(ClientKey current)
        {
            foreach (var cached in _clients)
            {
                if (cached.Key.Node != current.Node || cached.Key.Equals(current) ||
                    cached.Key.NodeEpoch > current.NodeEpoch ||
                    !((ICollection<KeyValuePair<ClientKey, Lazy<Task<RpcClientRuntime>>>>)_clients)
                        .Remove(cached))
                {
                    continue;
                }

                if (cached.Value.IsValueCreated)
                {
                    _ = DisposeWhenReadyAsync(cached.Value.Value);
                }
            }
        }

        private static async Task DisposeWhenReadyAsync(Task<RpcClientRuntime> runtimeTask)
        {
            try
            {
                var runtime = await runtimeTask.ConfigureAwait(false);
                await runtime.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
            }
        }

        private CancellationTokenSource? CreateConnectTimeout(CancellationToken cancellationToken)
        {
            if (!_options.ConnectTimeout.HasValue)
            {
                return null;
            }

            var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_options.ConnectTimeout.Value);
            return timeout;
        }

        private readonly struct ClientKey : IEquatable<ClientKey>
        {
            private ClientKey(
                NodeId node,
                long nodeEpoch,
                string endpointAddress,
                Guid clusterIncarnation,
                Guid nodeIncarnation,
                bool isExact)
            {
                Node = node;
                NodeEpoch = nodeEpoch;
                EndpointAddress = endpointAddress;
                ClusterIncarnation = clusterIncarnation;
                NodeIncarnation = nodeIncarnation;
                IsExact = isExact;
            }

            public NodeId Node { get; }

            public long NodeEpoch { get; }

            public string EndpointAddress { get; }

            public Guid ClusterIncarnation { get; }

            public Guid NodeIncarnation { get; }

            public bool IsExact { get; }

            public static ClientKey From(RouteLocation location)
            {
                var reference = location.NodeReference;
                return reference is null
                    ? new ClientKey(
                        location.Node,
                        location.NodeEpoch,
                        location.Endpoint.Address,
                        Guid.Empty,
                        Guid.Empty,
                        isExact: false)
                    : new ClientKey(
                        reference.Node,
                        0,
                        location.Endpoint.Address,
                        reference.Cluster.Value,
                        reference.Incarnation.Value,
                        isExact: true);
            }

            public bool Equals(ClientKey other) =>
                Node == other.Node
                && NodeEpoch == other.NodeEpoch
                && ClusterIncarnation == other.ClusterIncarnation
                && NodeIncarnation == other.NodeIncarnation
                && IsExact == other.IsExact
                && string.Equals(EndpointAddress, other.EndpointAddress, StringComparison.Ordinal);

            public override bool Equals(object? obj) => obj is ClientKey other && Equals(other);

            public override int GetHashCode() => HashCode.Combine(
                Node,
                NodeEpoch,
                EndpointAddress,
                ClusterIncarnation,
                NodeIncarnation,
                IsExact);
        }
    }
}
