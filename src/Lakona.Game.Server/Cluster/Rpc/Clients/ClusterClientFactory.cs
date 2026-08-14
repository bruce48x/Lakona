using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Lakona.Game.Cluster;
using Lakona.Rpc.Client;
using Lakona.Rpc.Core;
using Microsoft.Extensions.Logging;

namespace Lakona.Game.Cluster.Rpc
{
    internal sealed class ClusterClientFactory : IClusterClientFactory, IDisposable, IAsyncDisposable
    {
        private readonly ConcurrentDictionary<ClientKey, ClientEntry> _clients =
            new ConcurrentDictionary<ClientKey, ClientEntry>();
        private readonly ClusterRpcChannel _channel;
        private readonly IRpcSerializer _serializer;
        private readonly ClusterClientFactoryOptions _options;
        private readonly ILoggerFactory? _loggerFactory;
        private int _disposed;

        public ClusterClientFactory(
            ClusterRpcChannel channel,
            ClusterClientFactoryOptions? options = null,
            ILoggerFactory? loggerFactory = null)
        {
            _channel = channel ?? throw new ArgumentNullException(nameof(channel));
            _serializer = channel.Serializer;
            _options = options ?? new ClusterClientFactoryOptions();
            _loggerFactory = loggerFactory;
        }

        public async ValueTask<IRpcClient> GetClientAsync(
            RouteLocation target,
            CancellationToken cancellationToken = default)
        {
            if (target is null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            return await GetClientCoreAsync(target.Endpoint, ClientKey.From(target), cancellationToken)
                .ConfigureAwait(false);
        }

        public async ValueTask<IRpcClient> GetClientAsync(
            NodeEndpoint contact,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(contact);
            return await GetClientCoreAsync(contact, ClientKey.From(contact), cancellationToken)
                .ConfigureAwait(false);
        }

        private async ValueTask<IRpcClient> GetClientCoreAsync(
            NodeEndpoint endpoint,
            ClientKey key,
            CancellationToken cancellationToken)
        {
            while (true)
            {
                ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
                var candidate = new ClientEntry(entry => ConnectAsync(endpoint, key, entry));
                var selected = _clients.GetOrAdd(key, candidate);
                var runtimeTask = selected.RuntimeTask;
                try
                {
                    var runtime = await runtimeTask.WaitAsync(cancellationToken).ConfigureAwait(false);
                    if (!_clients.TryGetValue(key, out var cached) || !ReferenceEquals(cached, selected))
                    {
                        continue;
                    }

                    if (ReferenceEquals(candidate, selected))
                    {
                        RemoveSuperseded(key);
                    }

                    return runtime;
                }
                catch
                {
                    if (runtimeTask.IsCompleted && !runtimeTask.IsCompletedSuccessfully)
                    {
                        ((ICollection<KeyValuePair<ClientKey, ClientEntry>>)_clients)
                            .Remove(new KeyValuePair<ClientKey, ClientEntry>(key, selected));
                    }
                    throw;
                }
            }
        }

        private async Task<RpcClientRuntime> ConnectAsync(
            NodeEndpoint endpoint,
            ClientKey key,
            ClientEntry entry)
        {
            using var timeout = CreateConnectTimeout(CancellationToken.None);
            var effectiveToken = timeout?.Token ?? CancellationToken.None;
            var transport = await _channel.ConnectAsync(endpoint, effectiveToken).ConfigureAwait(false);
            var runtime = new RpcClientRuntime(
                transport,
                _serializer,
                _options.KeepAlive,
                _loggerFactory);
            runtime.Disconnected += _ => RemoveDisconnected(key, entry, runtime);
            try
            {
                await runtime.StartAsync(CancellationToken.None).ConfigureAwait(false);
                ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

                return runtime;
            }
            catch
            {
                await runtime.DisposeAsync().ConfigureAwait(false);
                throw;
            }
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
                    await (await client.Value.RuntimeTask.ConfigureAwait(false))
                        .DisposeAsync()
                        .ConfigureAwait(false);
                }
                catch
                {
                }
            }
        }

        public void Dispose()
        {
            DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        private void RemoveSuperseded(ClientKey current)
        {
            foreach (var cached in _clients)
            {
                if (!current.IsExact || !cached.Key.IsExact
                    || cached.Key.Node != current.Node || cached.Key.Equals(current) ||
                    !((ICollection<KeyValuePair<ClientKey, ClientEntry>>)_clients)
                        .Remove(cached))
                {
                    continue;
                }

                if (cached.Value.IsValueCreated)
                {
                    _ = DisposeWhenReadyAsync(cached.Value.RuntimeTask);
                }
            }
        }

        private void RemoveDisconnected(ClientKey key, ClientEntry entry, RpcClientRuntime runtime)
        {
            if (((ICollection<KeyValuePair<ClientKey, ClientEntry>>)_clients)
                .Remove(new KeyValuePair<ClientKey, ClientEntry>(key, entry)))
            {
                _ = DisposeRuntimeAsync(runtime);
            }
        }

        private static async Task DisposeRuntimeAsync(RpcClientRuntime runtime)
        {
            try
            {
                await runtime.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
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

        private sealed class ClientEntry
        {
            private readonly Lazy<Task<RpcClientRuntime>> _runtime;

            public ClientEntry(Func<ClientEntry, Task<RpcClientRuntime>> connect)
            {
                _runtime = new Lazy<Task<RpcClientRuntime>>(
                    () => connect(this),
                    LazyThreadSafetyMode.ExecutionAndPublication);
            }

            public bool IsValueCreated => _runtime.IsValueCreated;

            public Task<RpcClientRuntime> RuntimeTask => _runtime.Value;
        }

        private readonly struct ClientKey : IEquatable<ClientKey>
        {
            private ClientKey(
                NodeId? node,
                string endpointAddress,
                Guid clusterIncarnation,
                Guid nodeIncarnation,
                bool isExact)
            {
                Node = node;
                EndpointAddress = endpointAddress;
                ClusterIncarnation = clusterIncarnation;
                NodeIncarnation = nodeIncarnation;
                IsExact = isExact;
            }

            public NodeId? Node { get; }

            public string EndpointAddress { get; }

            public Guid ClusterIncarnation { get; }

            public Guid NodeIncarnation { get; }

            public bool IsExact { get; }

            public static ClientKey From(RouteLocation location)
            {
                var reference = location.NodeReference;
                return new ClientKey(
                    reference.Node,
                    location.Endpoint.Address,
                    reference.Cluster.Value,
                    reference.Incarnation.Value,
                    isExact: true);
            }

            public static ClientKey From(NodeEndpoint contact) => new(
                node: null,
                contact.Address,
                Guid.Empty,
                Guid.Empty,
                isExact: false);

            public bool Equals(ClientKey other) =>
                Node == other.Node
                && ClusterIncarnation == other.ClusterIncarnation
                && NodeIncarnation == other.NodeIncarnation
                && IsExact == other.IsExact
                && string.Equals(EndpointAddress, other.EndpointAddress, StringComparison.Ordinal);

            public override bool Equals(object? obj) => obj is ClientKey other && Equals(other);

            public override int GetHashCode() => HashCode.Combine(
                Node,
                EndpointAddress,
                ClusterIncarnation,
                NodeIncarnation,
                IsExact);
        }
    }
}
