using System.Collections.Concurrent;
using System.Threading.Channels;
using Lakona.Game.Server.Testing;
using Lakona.Rpc.Core;
using Lakona.Rpc.Transport.Loopback;

namespace Lakona.Game.Testing;

internal sealed class InMemoryClusterRpcTransport(
    string localNodeId,
    InMemoryClusterTransportHub hub) : ILakonaInProcessClusterTransport
{
    public string Scheme => InMemoryClusterTransportHub.Scheme;

    public ValueTask<ITransport> ConnectAsync(
        string endpoint,
        CancellationToken cancellationToken = default) =>
        hub.ConnectAsync(localNodeId, endpoint, cancellationToken);

    public ValueTask<IRpcConnectionAcceptor> ListenAsync(
        string endpoint,
        CancellationToken cancellationToken = default) =>
        hub.ListenAsync(localNodeId, endpoint, cancellationToken);
}

internal sealed class InMemoryClusterTransportHub(LakonaTestNetwork network) : IAsyncDisposable
{
    internal const string Scheme = "testcluster";

    private readonly ConcurrentDictionary<string, InMemoryClusterConnectionAcceptor> listeners =
        new(StringComparer.OrdinalIgnoreCase);

    internal ValueTask<IRpcConnectionAcceptor> ListenAsync(
        string nodeId,
        string endpoint,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = Key(endpoint);
        var acceptor = new InMemoryClusterConnectionAcceptor(
            key,
            nodeId,
            RemoveListener);
        if (!listeners.TryAdd(key, acceptor))
        {
            throw new InvalidOperationException(
                $"Lakona TestCluster endpoint '{key}' is already listening.");
        }

        return new ValueTask<IRpcConnectionAcceptor>(acceptor);
    }

    internal async ValueTask<ITransport> ConnectAsync(
        string sourceNodeId,
        string endpoint,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = Key(endpoint);
        if (!listeners.TryGetValue(key, out var listener))
        {
            throw new IOException(
                $"Lakona TestCluster endpoint '{key}' is not listening.");
        }

        network.ThrowIfBlocked(sourceNodeId, listener.NodeId);
        LoopbackTransport.CreatePair(out var client, out var server);
        // Accepted transports have already completed their transport-level connection.
        // RpcServerHost rejects disconnected accept results before starting negotiation.
        await server.ConnectAsync(cancellationToken).ConfigureAwait(false);
        var clientSide = new NetworkControlledTransport(
            client,
            network,
            sourceNodeId,
            listener.NodeId);
        var serverSide = new NetworkControlledTransport(
            server,
            network,
            listener.NodeId,
            sourceNodeId);
        try
        {
            await listener.EnqueueAsync(
                new RpcAcceptedConnection(
                    serverSide,
                    $"testcluster:{sourceNodeId}->{listener.NodeId}"),
                cancellationToken).ConfigureAwait(false);
            return clientSide;
        }
        catch
        {
            await clientSide.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var listener in listeners.Values.ToArray())
        {
            await listener.DisposeAsync().ConfigureAwait(false);
        }

        listeners.Clear();
    }

    private void RemoveListener(
        string key,
        InMemoryClusterConnectionAcceptor listener) =>
        listeners.TryRemove(new KeyValuePair<string, InMemoryClusterConnectionAcceptor>(
            key,
            listener));

    private static string Key(string endpoint)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
            || uri.Port <= 0)
        {
            throw new FormatException(
                $"Lakona TestCluster endpoint '{endpoint}' is invalid.");
        }

        var path = uri.AbsolutePath == "/" ? string.Empty : uri.AbsolutePath;
        return $"{uri.Scheme.ToLowerInvariant()}://{uri.Host}:{uri.Port}{path}";
    }

    private sealed class NetworkControlledTransport(
        ITransport inner,
        LakonaTestNetwork network,
        string sourceNodeId,
        string targetNodeId) : ITransport
    {
        public bool IsConnected => inner.IsConnected;

        public ValueTask ConnectAsync(CancellationToken cancellationToken = default)
        {
            network.ThrowIfBlocked(sourceNodeId, targetNodeId);
            return inner.ConnectAsync(cancellationToken);
        }

        public ValueTask SendFrameAsync(
            ReadOnlyMemory<byte> frame,
            CancellationToken cancellationToken = default)
        {
            network.ThrowIfBlocked(sourceNodeId, targetNodeId);
            return inner.SendFrameAsync(frame, cancellationToken);
        }

        public ValueTask<TransportFrame> ReceiveFrameAsync(
            CancellationToken cancellationToken = default) =>
            inner.ReceiveFrameAsync(cancellationToken);

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}

internal sealed class InMemoryClusterConnectionAcceptor(
    string key,
    string nodeId,
    Action<string, InMemoryClusterConnectionAcceptor> unregister) : IRpcConnectionAcceptor
{
    private readonly Channel<RpcAcceptedConnection> pending =
        Channel.CreateUnbounded<RpcAcceptedConnection>(new UnboundedChannelOptions
        {
            AllowSynchronousContinuations = false,
            SingleReader = true,
            SingleWriter = false
        });
    private int disposed;

    internal string NodeId { get; } = nodeId;

    public string ListenAddress => key;

    public async ValueTask<RpcAcceptedConnection> AcceptAsync(
        CancellationToken cancellationToken = default)
    {
        return await pending.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
    }

    internal ValueTask EnqueueAsync(
        RpcAcceptedConnection connection,
        CancellationToken cancellationToken) =>
        pending.Writer.WriteAsync(connection, cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        unregister(key, this);
        pending.Writer.TryComplete();
        while (pending.Reader.TryRead(out var connection))
        {
            await connection.Transport.DisposeAsync().ConfigureAwait(false);
        }
    }
}
