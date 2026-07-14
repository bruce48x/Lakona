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
        private readonly IClusterTransportFactory _transportFactory;
        private readonly IRpcSerializer _serializer;
        private readonly ClusterClientFactoryOptions _options;
        private int _disposed;

        public ClusterClientFactory(
            IClusterTransportFactory transportFactory,
            IRpcSerializer serializer,
            ClusterClientFactoryOptions? options = null)
        {
            _transportFactory = transportFactory ?? throw new ArgumentNullException(nameof(transportFactory));
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
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
            var endpoint = ClusterEndpoint.FromRouteLocation(target);
            using var timeout = CreateConnectTimeout(CancellationToken.None);
            var effectiveToken = timeout?.Token ?? CancellationToken.None;
            var transport = await _transportFactory.ConnectAsync(target, endpoint, effectiveToken).ConfigureAwait(false);
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
            private ClientKey(NodeId node, long nodeEpoch, string endpointAddress)
            {
                Node = node;
                NodeEpoch = nodeEpoch;
                EndpointAddress = endpointAddress;
            }

            public NodeId Node { get; }

            public long NodeEpoch { get; }

            public string EndpointAddress { get; }

            public static ClientKey From(RouteLocation location) =>
                new ClientKey(location.Node, location.NodeEpoch, location.Endpoint.Address);

            public bool Equals(ClientKey other) =>
                Node == other.Node && NodeEpoch == other.NodeEpoch &&
                string.Equals(EndpointAddress, other.EndpointAddress, StringComparison.Ordinal);

            public override bool Equals(object? obj) => obj is ClientKey other && Equals(other);

            public override int GetHashCode() => HashCode.Combine(Node, NodeEpoch, EndpointAddress);
        }
    }
}
