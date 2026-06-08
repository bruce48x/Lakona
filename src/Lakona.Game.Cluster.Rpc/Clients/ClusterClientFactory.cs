using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lakona.Game.Cluster;
using Lakona.Rpc.Client;
using Lakona.Rpc.Core;

namespace Lakona.Game.Cluster.Rpc
{
    public sealed class ClusterClientFactory : IClusterClientFactory, IAsyncDisposable
    {
        private readonly object _gate = new object();
        private readonly Dictionary<NodeId, CachedClient> _clients = new Dictionary<NodeId, CachedClient>();
        private readonly IClusterTransportFactory _transportFactory;
        private readonly IRpcSerializer _serializer;
        private readonly ClusterClientFactoryOptions _options;

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

            lock (_gate)
            {
                if (_clients.TryGetValue(target.Node, out var existing))
                {
                    if (existing.Matches(target))
                    {
                        return existing.Runtime;
                    }

                    _clients.Remove(target.Node);
                    _ = existing.Runtime.DisposeAsync();
                }
            }

            var endpoint = ClusterEndpoint.FromRouteLocation(target);
            using var timeout = CreateConnectTimeout(cancellationToken);
            var effectiveToken = timeout?.Token ?? cancellationToken;
            var transport = await _transportFactory.ConnectAsync(target, endpoint, effectiveToken).ConfigureAwait(false);
            var runtime = new RpcClientRuntime(
                transport,
                _serializer,
                _options.KeepAlive);
            var startTask = runtime.StartAsync(CancellationToken.None).AsTask();
            _ = startTask.ContinueWith(
                task => _ = task.Exception,
                TaskContinuationOptions.OnlyOnFaulted);

            lock (_gate)
            {
                if (_clients.TryGetValue(target.Node, out var existing))
                {
                    if (existing.Matches(target))
                    {
                        _ = runtime.DisposeAsync();
                        return existing.Runtime;
                    }

                    _clients.Remove(target.Node);
                    _ = existing.Runtime.DisposeAsync();
                }

                _clients[target.Node] = new CachedClient(runtime, target);
                return runtime;
            }
        }

        public async ValueTask DisposeAsync()
        {
            RpcClientRuntime[] clients;
            lock (_gate)
            {
                clients = new RpcClientRuntime[_clients.Count];
                var index = 0;
                foreach (var client in _clients.Values)
                {
                    clients[index] = client.Runtime;
                    index++;
                }

                _clients.Clear();
            }

            foreach (var client in clients)
            {
                await client.DisposeAsync().ConfigureAwait(false);
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

        private sealed class CachedClient
        {
            private readonly string _endpointAddress;

            public CachedClient(
                RpcClientRuntime runtime,
                RouteLocation location)
            {
                Runtime = runtime;
                NodeEpoch = location.NodeEpoch;
                _endpointAddress = location.Endpoint.Address;
            }

            public RpcClientRuntime Runtime { get; }

            public long NodeEpoch { get; }

            public bool Matches(RouteLocation location)
            {
                return NodeEpoch == location.NodeEpoch &&
                    string.Equals(_endpointAddress, location.Endpoint.Address, StringComparison.Ordinal);
            }
        }
    }
}
