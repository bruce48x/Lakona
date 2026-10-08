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
        private readonly object _lifecycleGate = new();
        // Evicted clients remain owned until their queued responses and transport cleanup finish.
        private readonly HashSet<ClientEntry> _ownedClients = new();
        private readonly ClusterRpcChannel _channel;
        private readonly IRpcSerializer _serializer;
        private readonly ClusterClientFactoryOptions _options;
        private readonly ILoggerFactory? _loggerFactory;
        private readonly CancellationTokenSource _shutdown = new();
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
                cancellationToken.ThrowIfCancellationRequested();
                ClientEntry candidate;
                ClientEntry selected;
                Task<RpcClientRuntime> runtimeTask;
                lock (_lifecycleGate)
                {
                    ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
                    candidate = new ClientEntry(entry => ConnectAsync(endpoint, key, entry));
                    selected = _clients.GetOrAdd(key, candidate);
                    _ownedClients.Add(selected);
                    runtimeTask = selected.RuntimeTask;
                }
                try
                {
                    var runtime = await runtimeTask.WaitAsync(cancellationToken).ConfigureAwait(false);
                    if (!_clients.TryGetValue(key, out var cached) || !ReferenceEquals(cached, selected))
                    {
                        continue;
                    }

                    // State notifications are asynchronous; do not depend on delivery to stop reuse.
                    if (runtime.ConnectionState != RpcClientConnectionState.Connected)
                    {
                        RemoveCached(key, selected);
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
                        RemoveCached(key, selected);
                        ReleaseOwnership(selected);
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
            RpcClientRuntime? runtime = null;
            try
            {
                using var timeout = CreateConnectTimeout(_shutdown.Token);
                var effectiveToken = timeout?.Token ?? _shutdown.Token;
                var transport = await _channel.ConnectAsync(endpoint, effectiveToken).ConfigureAwait(false);
                runtime = new RpcClientRuntime(
                    transport,
                    _serializer,
                    _options.KeepAlive,
                    _loggerFactory);
                var connectedRuntime = runtime;
                runtime.ConnectionStateChanged += change =>
                {
                    if (change.CurrentState is RpcClientConnectionState.Stopped or RpcClientConnectionState.Disposed)
                    {
                        RemoveCached(key, entry);
                    }
                };
                runtime.Disconnected += reason =>
                {
                    RemoveCached(key, entry);
                    _ = DisposeRuntimeAsync(entry, connectedRuntime);
                };
                await runtime.StartAsync(CancellationToken.None).ConfigureAwait(false);
                ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

                return runtime;
            }
            catch
            {
                try
                {
                    if (runtime is not null)
                    {
                        await runtime.DisposeAsync().ConfigureAwait(false);
                    }
                }
                finally
                {
                    ReleaseOwnership(entry);
                }
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            ClientEntry[] clients;
            lock (_lifecycleGate)
            {
                if (Interlocked.Exchange(ref _disposed, 1) != 0)
                {
                    return;
                }
                clients = new ClientEntry[_ownedClients.Count];
                _ownedClients.CopyTo(clients);
                _clients.Clear();
            }

            try
            {
                _shutdown.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }

            foreach (var client in clients)
            {
                await DisposeWhenReadyAsync(client).ConfigureAwait(false);
            }
            _shutdown.Dispose();
        }

        public void Dispose()
        {
            DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        private void RemoveSuperseded(ClientKey current)
        {
            var superseded = new List<ClientEntry>();
            lock (_lifecycleGate)
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
                    superseded.Add(cached.Value);
                }
            }
            foreach (var entry in superseded)
            {
                _ = DisposeWhenReadyAsync(entry);
            }
        }

        private void RemoveCached(ClientKey key, ClientEntry entry)
        {
            ((ICollection<KeyValuePair<ClientKey, ClientEntry>>)_clients)
                .Remove(new KeyValuePair<ClientKey, ClientEntry>(key, entry));
        }

        private async Task DisposeRuntimeAsync(ClientEntry entry, RpcClientRuntime runtime)
        {
            try
            {
                await runtime.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
            }
            finally
            {
                ReleaseOwnership(entry);
            }
        }

        private async Task DisposeWhenReadyAsync(ClientEntry entry)
        {
            try
            {
                var runtime = await entry.RuntimeTask.ConfigureAwait(false);
                await DisposeRuntimeAsync(entry, runtime).ConfigureAwait(false);
            }
            catch
            {
            }
            finally
            {
                ReleaseOwnership(entry);
            }
        }

        private void ReleaseOwnership(ClientEntry entry)
        {
            lock (_lifecycleGate)
            {
                _ownedClients.Remove(entry);
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
